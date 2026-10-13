/* Exercise the actual adapter tick with fake engine memory/command dispatch.
 * Peer discovery uses real loaded DLLs, exports and Windows module references. */
#include <windows.h>
#include "toolhelp_compat.h"
static int failSnapshot;
static HANDLE WINAPI TestSnapshot(DWORD flags,DWORD pid) {
    if(failSnapshot){SetLastError(ERROR_BAD_LENGTH);return INVALID_HANDLE_VALUE;}
    return CreateToolhelp32Snapshot(flags,pid);
}
#define CreateToolhelp32Snapshot TestSnapshot
#define PAW_RECOVERY_INTEGRATED
#define PAW_RECOVERY_TEST_COMMANDS
#include "reconnect.c"
static unsigned checks,saveCommands,pauseCommands;
#define CHECK(x) do {++checks;if(!(x)){printf("FAIL coordination line %d: %s\n",__LINE__,#x);exit(1);}}while(0)
static unsigned char admin[0x310],session[0x140],world[0x40],world2[0x40],resource[0x90],app[0x104],type[0x10],task[0x30];
static uintptr_t adminPtr,sessionPtr,worldPtr;
static int failCommand;
static int Command(const wchar_t *text) {
    CHECK(PawSaveBusy!=0&&g_step==SAVING); /* visible before command dispatch */
    if(!wcsncmp(text,L"GameSave ",9)){++saveCommands;return !failCommand;}
    CHECK(!wcscmp(text,L"GamePause 1"));++pauseCommands;return 1;
}
static void Setup(void) {
    Reset("test setup");
    memset(admin,0,sizeof(admin));memset(session,0,sizeof(session));memset(world,0,sizeof(world));
    memset(resource,0,sizeof(resource));memset(app,0,sizeof(app));memset(type,0,sizeof(type));memset(task,0,sizeof(task));
    adminPtr=(uintptr_t)admin;sessionPtr=(uintptr_t)session;worldPtr=(uintptr_t)world;
    g_adminGlobal=(uintptr_t)&adminPtr;g_sessionGlobal=(uintptr_t)&sessionPtr;g_worldGlobal=(uintptr_t)&worldPtr;
    g_netGlobal=0;g_app=(uintptr_t)app;
    *(uintptr_t*)(admin+0x2e4)=adminPtr;*(int*)(admin+0x300)=2;
    *(int*)(session+0xf0)=2;session[0x100]=1;
    *(uintptr_t*)(app+0xfc)=(uintptr_t)type;*(int*)(type+0xc)=1;
    *(uintptr_t*)(world+0x28)=(uintptr_t)resource; /* map source, no loaded-save chain */
    g_lastSession=g_lastWorld=g_roleSession=g_roleWorld=0;g_roleKnown=0;g_leaveCount=g_overflow=0;
    g_autoVote=0;g_testCommand=Command;saveCommands=pauseCommands=0;failCommand=failSnapshot=0;
}
static void Drop(int reason) {
    Leave *e=g_leaves+g_leaveCount++;memset(e,0,sizeof(*e));
    e->admin=adminPtr;e->session=sessionPtr;e->world=worldPtr;e->reason=reason;e->player=99;
}
static void Retry(void) {g_pendingProbe=0;TickRecovery();}
int main(int argc,char **argv) {
    HMODULE peer,invalid,adapter;volatile LONG *flag;unsigned i;wchar_t folder[MAX_PATH],path[MAX_PATH];HANDLE file;
    CHECK(argc==5);InitializeCriticalSection(&g_lock);
    CHECK(!OtherSaveBusy());InterlockedExchange(&PawSaveBusy,1);CHECK(!OtherSaveBusy());InterlockedExchange(&PawSaveBusy,0);
    peer=LoadLibraryA(argv[1]);CHECK(peer!=0);flag=(volatile LONG*)GetProcAddress(peer,"PawSaveBusy");CHECK(flag!=0);
    CHECK(!OtherSaveBusy());InterlockedExchange(flag,1);CHECK(OtherSaveBusy());
    Setup();Drop(5);TickRecovery();CHECK(g_pending&&g_step==IDLE&&!PawSaveBusy&&!saveCommands&&!pauseCommands);
    /* A second drop does not reset the waiting deadline. */
    {ULONGLONG since=g_pendingSince;Drop(5);Retry();CHECK(g_pendingSince==since&&!saveCommands);}
    InterlockedExchange(flag,0);Retry();CHECK(!g_pending&&g_step==SAVING&&PawSaveBusy&&saveCommands==1&&pauseCommands==1);
    CHECK(!g_task&&!Ptr(sessionPtr+0x134));Drop(5);Retry();CHECK(saveCommands==1); /* queued, no native task yet */
    Go(ENDING);CHECK(PawSaveBusy);Go(SELECTING);CHECK(PawSaveBusy);Go(CONFIRMING);CHECK(PawSaveBusy);
    Reset("finished");CHECK(!PawSaveBusy&&g_step==IDLE&&!g_pending);

    Setup();*(uintptr_t*)(session+0x134)=(uintptr_t)task;*(int*)(task+0x14)=1;
    Drop(5);TickRecovery();CHECK(g_pending&&!saveCommands&&!PawSaveBusy);
    *(int*)(task+0x14)=2;Retry();CHECK(g_pending&&!saveCommands); /* complete but still attached */
    *(uintptr_t*)(session+0x134)=0;Retry();CHECK(saveCommands==1&&PawSaveBusy);

    Setup();failCommand=1;Drop(5);TickRecovery();CHECK(saveCommands==1&&!pauseCommands&&!PawSaveBusy&&g_step==IDLE&&!g_pending);
    Setup();*(uintptr_t*)(world+0x28)=0;Drop(5);TickRecovery();CHECK(!saveCommands&&!PawSaveBusy&&!g_pending);
    Setup();failSnapshot=1;Drop(5);TickRecovery();CHECK(!saveCommands&&g_pending);
    failSnapshot=0;Retry();CHECK(saveCommands==1&&PawSaveBusy);

    Setup();InterlockedExchange(flag,1);Drop(5);TickRecovery();CHECK(g_pending);
    g_pendingSince=Now()-120000;Retry();CHECK(!g_pending&&!saveCommands&&!PawSaveBusy);
    InterlockedExchange(flag,0);Retry();CHECK(!saveCommands); /* expired drop is not resurrected */

    /* Never apply a retained leave to a new lobby, role, session or world. */
    for(i=0;i<6;++i){
        Setup();InterlockedExchange(flag,1);Drop(5);TickRecovery();CHECK(g_pending);
        if(i==0)*(int*)(session+0xf0)=1;
        if(i==1)*(uintptr_t*)(admin+0x2e4)=0;
        if(i==2)++worldPtr;
        if(i==3)*(int*)(session+0x128)=1;
        if(i==4)sessionPtr=0;
        if(i==5)admin[0x2ed]=1;
        InterlockedExchange(flag,0);Retry();CHECK(!g_pending&&!saveCommands&&!PawSaveBusy);
    }
    Setup();InterlockedExchange(flag,1);Drop(5);TickRecovery();CHECK(g_pending);
    memset(world2,0,sizeof(world2));*(uintptr_t*)(world2+0x28)=(uintptr_t)resource;worldPtr=(uintptr_t)world2;
    Drop(5);InterlockedExchange(flag,0);Retry();CHECK(saveCommands==1&&g_recoveryWorld==worldPtr&&!g_pending);
    Setup();Drop(11);TickRecovery();CHECK(!g_pending&&!saveCommands);
    Drop(5);g_leaves[0].deliberate=1;TickRecovery();CHECK(!g_pending&&!saveCommands);

    /* Completion keeps coordination through the lobby transition. */
    Setup();Drop(5);TickRecovery();CHECK(PawSaveBusy);
    CHECK(GetTempPathW(MAX_PATH,folder));CHECK(GetTempFileNameW(folder,L"psc",0,path));
    file=CreateFileW(path,GENERIC_WRITE,0,0,OPEN_EXISTING,0,0);CHECK(file!=INVALID_HANDLE_VALUE);
    {DWORD n;CHECK(WriteFile(file,"x",1,&n,0)&&n==1);}CloseHandle(file);
    wcscpy(g_path,path);*(LONG*)(task+4)=2;*(int*)(task+0x14)=2;g_task=(uintptr_t)task;
    *(uintptr_t*)(session+0x134)=(uintptr_t)task;TickRecovery();CHECK(g_step==SAVING&&PawSaveBusy);
    *(uintptr_t*)(session+0x134)=0;TickRecovery();CHECK(g_step==ENDING&&PawSaveBusy&&!g_task&&*(LONG*)(task+4)==1);
    Go(CONFIRMING);*(int*)(session+0xf0)=1;*(uintptr_t*)(session+0x68)=(uintptr_t)g_name;
    TickRecovery();CHECK(g_step==IDLE&&!PawSaveBusy);CHECK(DeleteFileW(path));

    Setup();Drop(5);TickRecovery();*(LONG*)(task+4)=2;*(int*)(task+0x14)=3;g_task=(uintptr_t)task;
    TickRecovery();CHECK(g_step==IDLE&&!PawSaveBusy&&!g_task);
    Setup();Drop(5);TickRecovery();g_since=Now()-120001;TickRecovery();CHECK(g_step==IDLE&&!PawSaveBusy);
    Setup();Drop(5);TickRecovery();sessionPtr=0;TickRecovery();CHECK(g_step==IDLE&&!PawSaveBusy);

    Setup();invalid=LoadLibraryA(argv[2]);CHECK(invalid!=0);CHECK(OtherSaveBusy());Drop(5);TickRecovery();CHECK(g_pending&&!saveCommands);
    CHECK(FreeLibrary(invalid));Retry();CHECK(saveCommands==1);Reset("test fixture unload");
    CHECK(FreeLibrary(peer));CHECK(!OtherSaveBusy()); /* no cached, unpinned pointers */
    for(i=3;i<5;++i){
        adapter=LoadLibraryA(argv[i]);CHECK(adapter!=0);flag=(volatile LONG*)GetProcAddress(adapter,"PawSaveBusy");CHECK(flag!=0&&*flag==0);
        CHECK(!OtherSaveBusy());InterlockedExchange(flag,1);CHECK(OtherSaveBusy());InterlockedExchange(flag,0);CHECK(!OtherSaveBusy());
        CHECK(FreeLibrary(adapter));CHECK(!OtherSaveBusy());
    }
    printf("PASS %u save coordination checks (real DLL exports, adapter tick, no game)\n",checks);return 0;
}
