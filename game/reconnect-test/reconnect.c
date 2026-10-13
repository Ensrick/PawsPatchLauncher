/* Local recovery experiment for Kohan II 1.3.72.
 * Adapted from Ensrick/kohan2-mod-manager v0.1.0-beta.12 (MIT),
 * commit 9092a0dd00d0cc74d7c9f4ef85292d8ab0add77b.
 * See THIRD_PARTY.md and LICENSE.Ensrick. This is NOT hot join.
 * Native commands are issued only by the loader on the game's main thread.
 */
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdarg.h>
#include <string.h>
#include <wchar.h>
#include <stdlib.h>
#include "guards.h"
#define FC __attribute__((fastcall))
#define EXPORT __declspec(dllexport)

typedef struct { unsigned api; uintptr_t exeBase; const wchar_t *gameDir,*modsDir; void (__cdecl *log)(const char*,const char*,...); } Host;
/* TCC has no thiscall emitter: reserve the second fastcall register explicitly.
 * ECX=this, EDX=unused, all real arguments stay on the callee-cleaned stack. */
typedef void (FC *LeaveFn)(void*,void*,void*,int);
typedef void (FC *RequestFn)(void*,void*,void*,int);
typedef void (FC *CreateFn)(void*,void*,int);
typedef void* (FC *StringFn)(void*,void*,const wchar_t*);
typedef void (FC *ReleaseFn)(void*,void*);
typedef void (FC *RunFn)(void*,void*,void*,int);
typedef void* (FC *CtorFn)(void*,void*,void*);
typedef void (FC *IssueFn)(void*,void*,int);
typedef void* (FC *DtorFn)(void*,void*,int);
typedef int (FC *WantedFn)(void*,void*);
typedef unsigned char (__cdecl *FromResourceFn)(void*);
typedef struct { uintptr_t player,admin,world; DWORD when; } Intent;
typedef struct { uintptr_t admin,session,world,player; int reason,deliberate; } Leave;
typedef struct { uintptr_t admin,session,world,net; int state,hostKind,type,host,backup,disconnecting,network,countdown; } Snapshot;
static const Host *g_host;
static uintptr_t g_base,g_adminGlobal,g_sessionGlobal,g_worldGlobal,g_consoleGlobal,g_netGlobal,g_app;
static CRITICAL_SECTION g_lock;
static Intent g_intents[32];
static Leave g_leaves[32];
static unsigned g_leaveCount,g_overflow;
static DWORD g_thread;
static LONG g_ticking;
static int g_active;
static uintptr_t g_lastSession,g_lastWorld;
static LeaveFn g_leave;
static RequestFn g_request;
static CreateFn g_create;
static WantedFn g_wanted;
static FromResourceFn g_fromResource;

enum { IDLE, SAVING, ENDING, SELECTING, CONFIRMING };
static int g_step,g_tookOver,g_sends;
static ULONGLONG g_since,g_sent,g_arrived,g_down,g_lastVote;
static int g_votes;
static int g_autoVote,g_roleKnown,g_originalHost;
static uintptr_t g_roleSession,g_roleWorld;
static uintptr_t g_recoverySession,g_recoveryWorld,g_task;
static wchar_t g_name[128],g_data[MAX_PATH],g_path[MAX_PATH];
static unsigned g_serial;
static ULONGLONG Now(void) {
    typedef ULONGLONG (WINAPI *Fn)(void);
    static Fn fn; if(!fn)fn=(Fn)GetProcAddress(GetModuleHandleA("kernel32.dll"),"GetTickCount64");
    return fn?fn():(ULONGLONG)GetTickCount();
}

static void Log(const char *fmt,...) {
    char text[1024];va_list ap;va_start(ap,fmt);vsnprintf(text,sizeof(text),fmt,ap);va_end(ap);
    text[sizeof(text)-1]=0;if(g_host&&g_host->log)g_host->log("paws-recovery", "%s",text);
}
static int Readable(uintptr_t p,unsigned n) {
    MEMORY_BASIC_INFORMATION m;
    if(!p||p+n<p||!VirtualQuery((void*)p,&m,sizeof(m))||m.State!=MEM_COMMIT||(m.Protect&(PAGE_GUARD|PAGE_NOACCESS)))return 0;
    return p+n<=(uintptr_t)m.BaseAddress+m.RegionSize;
}
static uintptr_t Ptr(uintptr_t p) { return Readable(p,4)?*(const uintptr_t*)p:0; }
static int Int(uintptr_t p) { return Readable(p,4)?*(const int*)p:0; }
static int Byte(uintptr_t p) { return Readable(p,1)?*(const unsigned char*)p:0; }
static int TextEqual(uintptr_t p,const wchar_t *s) {
    unsigned i;for(i=0;i<128;++i){if(!Readable(p+i*2,2))return 0;if(((const wchar_t*)p)[i]!=s[i])return 0;if(!s[i])return 1;}return 0;
}
static int SnapshotRead(Snapshot *s) {
    uintptr_t type;memset(s,0,sizeof(*s));
    s->admin=Ptr(g_adminGlobal);s->session=Ptr(g_sessionGlobal);s->world=Ptr(g_worldGlobal);s->net=Ptr(g_netGlobal);
    if(!Readable(s->admin,0x308)||!Readable(s->session,0x138))return 0;
    s->state=Int(s->session+0xf0);s->host=Ptr(s->admin+0x2e4)==s->admin;
    s->backup=Byte(s->admin+0x2f4);s->disconnecting=Byte(s->admin+0x2ed);
    s->hostKind=Int(s->admin+0x300);s->network=Byte(s->session+0x100);s->countdown=Int(s->session+0x128);
    type=Ptr(g_app+0xfc);s->type=type?Int(type+0xc):-1;return 1;
}
static int RunningHost(const Snapshot *s) {return s->host&&!s->disconnecting&&s->network&&s->world&&s->state==2&&s->type==1;}
/* Backup mode means stalled simulation, not host migration. Remember the local
 * role at match entry, before any leave decree can elect a replacement host. */
static void TrackRole(const Snapshot *s) {
    if(g_roleSession!=s->session||g_roleWorld!=s->world||s->state==1||!s->network){
        g_roleKnown=0;g_roleSession=s->session;g_roleWorld=s->world;
    }
    if(!g_roleKnown&&s->network&&s->world&&s->state==2&&s->type==1){
        g_originalHost=s->host;g_roleKnown=1;Log("MATCH_ROLE: original_host=%d",g_originalHost);
    }
}
static int Validate(uintptr_t base) {
    unsigned i,j,k;unsigned char expected[700];
    for(i=0;i<sizeof(guards)/sizeof(guards[0]);++i){
        const Guard *g=guards+i;if(g->size>sizeof(expected)||!Readable(base+g->rva,g->size))return 0;
#ifdef PAW_RECOVERY_INTEGRATED
        /* The normal helper owns R2 transfer and the null-player guard. Never
         * validate their already-replaced instructions as stock, or install a
         * second copy. Recovery's own three call sites remain exact-checked. */
        if(!strcmp(g->name,"wanted_type")||!strcmp(g->name,"write_call")||!strcmp(g->name,"schedule_call")||
           !strcmp(g->name,"task_block")||!strcmp(g->name,"active_clients")||
           !strcmp(g->name,"join_ip_call")||!strcmp(g->name,"join_port_call")||!strcmp(g->name,"widget_text"))continue;
#endif
        memcpy(expected,g->bytes,g->size);
        for(j=0;j<g->count;++j){k=g->fixups[j];if(k+4>g->size)return 0;*(unsigned*)(expected+k)+=(unsigned)base-0x460000;}
        if(memcmp((void*)(base+g->rva),expected,g->size)){Log("REFUSED: 1.3.72 code differs at %s +%x",g->name,g->rva);return 0;}
    }return 1;
}
static uintptr_t CallTarget(uintptr_t site) {return Readable(site,5)&&*(unsigned char*)site==0xe8?site+5+*(int*)(site+1):0;}
static int Redirect(unsigned rva,uintptr_t before,uintptr_t after) {
    uintptr_t site=g_base+rva;DWORD old,ignored;int rel=(int)(after-site-5);
    if(CallTarget(site)!=before||!VirtualProtect((void*)(site+1),4,PAGE_EXECUTE_READWRITE,&old))return 0;
    memcpy((void*)(site+1),&rel,4);VirtualProtect((void*)(site+1),4,old,&ignored);FlushInstructionCache(GetCurrentProcess(),(void*)site,5);return 1;
}
/* Same null-player guard as game/engine-crash-fixes, restricted to the verified
 * active-client count. A rejected premature join may leave a null player here. */
#ifndef PAW_RECOVERY_INTEGRATED
static int InstallClientGuard(void) {
    static const unsigned char original[]={0x8b,0x40,0x0c,0x83,0x78,0x28,0};
    unsigned char code[]={0x85,0xc0,0x74,0x10,0x8b,0x40,0x0c,0x85,0xc0,0x74,0x09,
        0x83,0x78,0x28,0,0xe9,0,0,0,0,0x31,0xc0,0xe9,0,0,0,0};
    unsigned char patch[]={0xe9,0,0,0,0,0x90,0x90};
    uintptr_t site=g_base+0x14c047,back=site+7;unsigned char *cave;DWORD old,ignored;
    if(memcmp((void*)site,original,sizeof(original)))return 0;
    cave=(unsigned char*)VirtualAlloc(0,sizeof(code),MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);if(!cave)return 0;
    *(int*)(code+16)=(int)(back-(uintptr_t)cave-20);*(int*)(code+23)=(int)(back-(uintptr_t)cave-27);
    memcpy(cave,code,sizeof(code));
    if(!VirtualProtect(cave,sizeof(code),PAGE_EXECUTE_READ,&old)){VirtualFree(cave,0,MEM_RELEASE);return 0;}
    FlushInstructionCache(GetCurrentProcess(),cave,sizeof(code));*(int*)(patch+1)=(int)((uintptr_t)cave-site-5);
    if(!VirtualProtect((void*)site,7,PAGE_EXECUTE_READWRITE,&old)){VirtualFree(cave,0,MEM_RELEASE);return 0;}
    memcpy((void*)site,patch,7);VirtualProtect((void*)site,7,old,&ignored);FlushInstructionCache(GetCurrentProcess(),(void*)site,7);
    return 1;
}
#endif
static int Run(const wchar_t *command) {
    uintptr_t console=Ptr(g_consoleGlobal);wchar_t *text=0;
    if(!Readable(console,4)||GetCurrentThreadId()!=g_thread)return 0;
    ((StringFn)(g_base+0x205de))(&text,0,command);
    if(!text)return 0;((RunFn)(g_base+0x1f9963))((void*)console,0,&text,0);
    ((ReleaseFn)(g_base+0x21375))((char*)text-16,0);return 1;
}
static void RememberIntent(uintptr_t player,uintptr_t admin,uintptr_t world,DWORD now) {
    unsigned i,slot=0;DWORD oldest=0;if(!player)return;
    for(i=0;i<32;++i){if(g_intents[i].player==player||!g_intents[i].player){slot=i;break;}if(now-g_intents[i].when>=oldest){oldest=now-g_intents[i].when;slot=i;}}
    g_intents[slot].player=player;g_intents[slot].admin=admin;g_intents[slot].world=world;g_intents[slot].when=now;
}
static int ConsumeIntent(uintptr_t player,uintptr_t admin,uintptr_t world,DWORD now) {
    unsigned i;for(i=0;i<32;++i)if(g_intents[i].player==player&&g_intents[i].admin==admin&&g_intents[i].world==world){
        int yes=(DWORD)(now-g_intents[i].when)<10000;memset(g_intents+i,0,sizeof(Intent));return yes;
    }return 0;
}
static void FC RequestHook(void *conn,void *unused,void *stream,int sequence) {
    uintptr_t player=Ptr((uintptr_t)conn+0xc);(void)unused;
    EnterCriticalSection(&g_lock);RememberIntent(player,Ptr(g_adminGlobal),Ptr(g_worldGlobal),GetTickCount());LeaveCriticalSection(&g_lock);
    g_request(conn,0,stream,sequence);
}
static void FC LeaveHook(void *admin,void *unused,void *player,int reason) {
    Leave e;(void)unused;memset(&e,0,sizeof(e));e.admin=(uintptr_t)admin;e.player=(uintptr_t)player;
    e.session=Ptr(g_sessionGlobal);e.world=Ptr(g_worldGlobal);e.reason=reason;
    EnterCriticalSection(&g_lock);e.deliberate=ConsumeIntent(e.player,e.admin,e.world,GetTickCount());LeaveCriticalSection(&g_lock);
    g_leave(admin,0,player,reason);
    if(!player)return;EnterCriticalSection(&g_lock);
    if(g_leaveCount<32)g_leaves[g_leaveCount++]=e;else ++g_overflow;
    LeaveCriticalSection(&g_lock);
}
static void ReleaseTask(void) {
    uintptr_t task=g_task;g_task=0;if(!task)return;
    if(InterlockedDecrement((LONG*)(task+4))==0){uintptr_t dtor=Ptr(Ptr(task));((DtorFn)dtor)((void*)task,0,1);}
}
static void FC CreateHook(void *session,void *unused,int isMap) {
    uintptr_t task;(void)unused;g_create(session,0,isMap);
    if(g_step!=SAVING||isMap||(uintptr_t)session!=g_recoverySession||g_task||!TextEqual(Ptr((uintptr_t)session+0x68),g_name))return;
    task=Ptr((uintptr_t)session+0x134);if(!Readable(task,0x2c))return;
    InterlockedIncrement((LONG*)(task+4));g_task=task;Log("SAVE_TASK captured; waiting for native completion");
}
/* KKC_ResourceManagerTaskSaveGame::SaveGame (1c1a15) walks the world resource
 * through its create settings to the source resource and asserts that each
 * source header is loaded. A PC that received the match's save over the network
 * has no such header: the assertion then waits on the ASYNC thread holding a lock
 * the main thread needs, and the game ends with "timeout waiting for mutex"
 * (issue #10). The same walk, read-only: 1 when the save task can run. */
static int SaveSourcesReady(uintptr_t world) {
    uintptr_t res=Ptr(world+0x28),settings;unsigned i;
    if(!Readable(res,0x8c))return 0;if(!(Int(res+8)&0x10))return 1;
    for(i=0;i<16;++i){
        settings=Ptr(res+0x88);if(!Readable(settings,0x54))return 0;
        if(!g_fromResource((void*)settings))return 1;
        res=Ptr(settings+0x50);if(!Readable(res,0x8c)||!(Int(res+8)&2))return 0;
    }return 0;
}
static void Reset(const char *why) {if(g_step!=IDLE)Log("recovery stopped: %s (step %d)",why,g_step);ReleaseTask();g_step=IDLE;g_name[0]=0;}
static void Go(int step) {g_step=step;g_since=Now();}
static void StartRecovery(const Snapshot *s,int reason) {
    SYSTEMTIME t;wchar_t cmd[180];
    if(!SaveSourcesReady(s->world)){Log("DROP: reason=%d; not saving: this PC received the match's save and the game cannot save it here; match continues",reason);return;}
    GetLocalTime(&t);
    _snwprintf(g_name,128,L"Save/Paw Resume %04u-%02u-%02u %02u%02u%02u-%03u-%lu-%u.RSG",t.wYear,t.wMonth,t.wDay,t.wHour,t.wMinute,t.wSecond,t.wMilliseconds,GetCurrentProcessId(),++g_serial);
    _snwprintf(g_path,MAX_PATH,L"%ls\\%ls",g_data,g_name);g_path[MAX_PATH-1]=0;
    {wchar_t *p;for(p=g_path;*p;++p)if(*p==L'/')*p=L'\\';}
    g_recoverySession=s->session;g_recoveryWorld=s->world;g_tookOver=!g_roleKnown||!g_originalHost;Go(SAVING);
    _snwprintf(cmd,180,L"GameSave \"%ls\"",g_name);cmd[179]=0;
    Log("DROP: reason=%d; host saving and pausing (backup=%d, replacement_host=%d)",reason,s->backup,g_tookOver);
    if(!Run(cmd)){Reset("cannot queue save");return;}
    if(!Run(L"GamePause 1"))Log("could not queue pause");
}
static int FileReady(void) {
    HANDLE f=CreateFileW(g_path,GENERIC_READ,FILE_SHARE_READ,0,OPEN_EXISTING,0,0);DWORD low,high=0;int ok;
    if(f==INVALID_HANDLE_VALUE)return 0;SetLastError(0);low=GetFileSize(f,&high);ok=(low!=INVALID_FILE_SIZE||GetLastError()==0)&&(low||high);CloseHandle(f);return ok;
}
static void SelectSave(void) {
    wchar_t *text=0;struct {uintptr_t vtable;wchar_t *name;} order={0,0};
    ((StringFn)(g_base+0x205de))(&text,0,g_name);if(!text)return;
    ((CtorFn)(g_base+0x121bd0))(&order,0,&text);((IssueFn)(g_base+0x122822))(&order,0,0);
    if(order.name)((ReleaseFn)(g_base+0x21375))((char*)order.name-16,0);
    ((ReleaseFn)(g_base+0x21375))((char*)text-16,0);
}
static int ConnectionDown(uintptr_t net) {
    uintptr_t p=Ptr(net+0x70);unsigned i;for(i=0;p&&i<64;++i){uintptr_t conn=Ptr(Ptr(p)+0xa0);if(Readable(conn,0xa0)&&!Byte(conn+0x98))return 1;p=Ptr(p+4);}return 0;
}
static int VoteDue(const Snapshot *s,ULONGLONG now,int down) {
    if(!g_autoVote||s->state!=2||s->type!=1||s->disconnecting||!s->network||!s->backup||!s->world||!down){g_down=0;g_votes=0;g_lastVote=0;return 0;}
    if(!g_down){g_down=now;Log("connection lost; waiting 20 seconds before native drop vote");return 0;}
    return now-g_down>=20000&&g_votes<3&&(!g_lastVote||now-g_lastVote>=30000);
}
static void Vote(const Snapshot *s,ULONGLONG now) {
    if(VoteDue(s,now,ConnectionDown(s->net))&&Run(L"CSDKickLaggingPlayers 0")){++g_votes;g_lastVote=now;Log("native drop vote %d",g_votes);}
}
static void TickRecovery(void) {
    Snapshot s;Leave events[32];unsigned count,overflow,i;ULONGLONG now=Now();
    static int lastState=-1,lastHost=-1,lastBackup=-1,lastDisconnecting=-1;
    if(!SnapshotRead(&s))return;
    if(s.state!=lastState||s.host!=lastHost||s.backup!=lastBackup||s.disconnecting!=lastDisconnecting){
        Log("STATE: session=%x world=%x state=%d host=%d backup=%d disconnecting=%d",s.session,s.world,s.state,s.host,s.backup,s.disconnecting);
        lastState=s.state;lastHost=s.host;lastBackup=s.backup;lastDisconnecting=s.disconnecting;
    }
    TrackRole(&s);
    EnterCriticalSection(&g_lock);count=g_leaveCount;memcpy(events,g_leaves,count*sizeof(Leave));g_leaveCount=0;overflow=g_overflow;g_overflow=0;LeaveCriticalSection(&g_lock);
    if(overflow)Log("leave queue overflow=%u; no speculative recovery",overflow);
    if(s.session!=g_lastSession||s.world!=g_lastWorld){
        g_down=0;g_votes=0;g_lastVote=0;
        EnterCriticalSection(&g_lock);memset(g_intents,0,sizeof(g_intents));LeaveCriticalSection(&g_lock);
        g_lastSession=s.session;g_lastWorld=s.world;
    }
    for(i=0;i<count;++i){const Leave *e=events+i;
        if(e->session!=s.session||e->world!=s.world||e->admin!=s.admin||!RunningHost(&s))continue;
        if(e->deliberate){Log("intentional player exit ignored (reason=%d)",e->reason);continue;}
        if(e->reason==11){Log("explicit kick ignored");continue;}
        if(g_step==IDLE)StartRecovery(&s,e->reason);else Log("another player dropped during recovery");
    }
    if(g_step==IDLE){Vote(&s,now);return;}
    if(s.session!=g_recoverySession||!s.host||s.disconnecting){Reset("session or host changed");return;}
    if(now-g_since>120000){Reset("step timed out; keep existing match/save");return;}
    if(g_step==SAVING){
        int state;if(s.world!=g_recoveryWorld||(s.state!=2&&s.state!=4)){Reset("match changed while saving");return;}
        if(!g_task)return;state=Int(g_task+0x14);
        if(state==3){Reset("native save task failed");return;}if(state!=2)return;
        if(!FileReady()||Ptr(s.session+0x134))return;
        ReleaseTask();Log("SAVE_FINISHED: native task succeeded and file is closed");
        if(g_tookOver){Log("HOST_LOST: resume save ready; new host keeps paused match; reload save manually");Reset("backup host save complete");return;}
        Go(ENDING);return;
    }
    if(g_step==ENDING){
        if(s.world!=g_recoveryWorld||s.state!=2||!s.network||s.countdown>0||s.type!=1){Reset("cannot safely return to lobby");return;}
        if(s.backup)return; /* Wait for the native vote/decree to restore synchronization. */
        Log("RETURN_TO_LOBBY after completed save");Go(SELECTING);g_arrived=0;((void(__cdecl*)(void))(g_base+0xc3efb))();return;
    }
    if(g_step==SELECTING){if(s.state!=1){g_arrived=0;return;}if(!g_arrived)g_arrived=now;if(now-g_arrived<1500)return;g_sends=0;g_sent=0;Go(CONFIRMING);return;}
    if(g_step==CONFIRMING){
        if(s.state!=1){Reset("left lobby before resume selection");return;}
        if(TextEqual(Ptr(s.session+0x68),g_name)){
            Log("RESUME_READY: player may rejoin; host can launch the selected save");
            if(s.hostKind!=2)Run(L"ScoringExit");Reset("ready in lobby");return;
        }
        if(g_sent&&now-g_sent<2000)return;if(g_sends>=5){Reset("choose Paw Resume in lobby manually");return;}
        SelectSave();++g_sends;g_sent=now;
    }
}

/* Ensrick's bounded file-transfer pacing; stock wire format and block size.
 * This isolated test does not load Paw's R2 helper or another transfer plugin. */
#ifndef PAW_RECOVERY_INTEGRATED
typedef struct { uintptr_t conn; float last,tokens,seen; } Bucket;
static Bucket g_buckets[16];
static int Eligible(uintptr_t admin,uintptr_t conn) {
    return Readable(admin,0x308)&&Readable(conn,0x430)&&Ptr(admin+0x2e4)==admin&&!Byte(admin+0x2ec)&&!Byte(admin+0x2ed)&&Byte(conn+0x98)&&!Byte(conn+0x99)&&Int(conn+0x3c)==2;
}
static int Take(Bucket *b,float clock,int consume) {
    if(clock<b->last){b->tokens=1;}else b->tokens+=(clock-b->last)*60;
    if(b->tokens>6)b->tokens=6;b->last=clock;if(b->tokens<1)return 0;if(consume)b->tokens-=1;return 1;
}
static int Decide(void *conn,int writing) {
    int type=g_wanted(conn,0);uintptr_t admin=Ptr(g_adminGlobal),net=Ptr(g_netGlobal);unsigned i;Bucket *b=0;float clock;
    if(!Eligible(admin,(uintptr_t)conn)||!Readable(net+0x50,4))return type;
    clock=*(float*)(net+0x50);if(!(clock>=0&&clock<1.0e9f))return type;
    for(i=0;i<16;++i)if(g_buckets[i].conn==(uintptr_t)conn){b=g_buckets+i;break;}
    if(b&&(clock<b->seen||clock-b->seen>5)){memset(b,0,sizeof(*b));b=0;}
    if(!b)for(i=0;i<16;++i)if(!g_buckets[i].conn||clock-g_buckets[i].seen>5){b=g_buckets+i;memset(b,0,sizeof(*b));b->conn=(uintptr_t)conn;b->tokens=1;b->last=clock;break;}
    if(!b)return type;b->seen=clock;if(type)return type;return Take(b,clock,writing)?5:0;
}
static int FC WriteHook(void *conn,void *unused){(void)unused;return Decide(conn,1);}
static int FC ScheduleHook(void *conn,void *unused){(void)unused;return Decide(conn,0);}

/* Optional direct-IP support for owned local test processes only. */
static wchar_t *g_joinIp,*g_joinPort;
static void* FC IpHook(void *widget,void *unused){(void)widget;(void)unused;return &g_joinIp;}
static void* FC PortHook(void *widget,void *unused){(void)widget;(void)unused;return &g_joinPort;}
#endif

EXPORT int __cdecl K2Plugin_Start(const Host *host) {
    unsigned i,done=0,count;
#ifndef PAW_RECOVERY_INTEGRATED
    wchar_t join[80],*colon;DWORD n;
#endif
    struct {unsigned site,target;uintptr_t hook;} hooks[]={
        {0x1503ea,0x151cb5,(uintptr_t)RequestHook},{0x15708c,0x14b7c5,(uintptr_t)LeaveHook},
        {0x162c3a,0x16a9f6,(uintptr_t)CreateHook}
#ifndef PAW_RECOVERY_INTEGRATED
        ,{0x150273,0x150009,(uintptr_t)WriteHook},{0x15c568,0x150009,(uintptr_t)ScheduleHook}
#endif
    };
    count=sizeof(hooks)/sizeof(hooks[0]);
    if(!host||host->api!=1||g_active)return 1;g_host=host;g_base=host->exeBase;
#ifndef PAW_RECOVERY_INTEGRATED
    g_thread=GetCurrentThreadId();
#endif
    if(!Validate(g_base))return 1;
#ifndef PAW_RECOVERY_INTEGRATED
    n=GetEnvironmentVariableW(L"PAWS_RECOVERY_DATA",g_data,MAX_PATH);
    if(!n||n>=MAX_PATH-130||GetFileAttributesW(g_data)==INVALID_FILE_ATTRIBUTES){Log("REFUSED: launch through PawsReconnectTest.exe with private saves");return 1;}
    {wchar_t vote[4];g_autoVote=GetEnvironmentVariableW(L"PAWS_RECOVERY_AUTO_VOTE",vote,4)==1&&vote[0]==L'1';}
#endif
    g_adminGlobal=Ptr(g_base+0x150009+5);g_netGlobal=Ptr(g_base+0x150009+0x21);
    g_sessionGlobal=Ptr(g_base+0x15bbe3+1);g_consoleGlobal=Ptr(g_base+0x13df06+2);
    g_worldGlobal=Ptr(g_base+0x162ac1+1);g_app=Ptr(g_base+0xc3efb+14);
    g_leave=(LeaveFn)(g_base+0x14b7c5);g_request=(RequestFn)(g_base+0x151cb5);g_create=(CreateFn)(g_base+0x16a9f6);g_wanted=(WantedFn)(g_base+0x150009);
    g_fromResource=(FromResourceFn)(g_base+0x16bf8f);
    InitializeCriticalSection(&g_lock);
    for(i=0;i<count;++i)if(CallTarget(g_base+hooks[i].site)!=g_base+hooks[i].target){Log("REFUSED: existing hook at %x",hooks[i].site);return 1;}
    for(i=0;i<count;++i){if(!Redirect(hooks[i].site,g_base+hooks[i].target,hooks[i].hook))break;++done;}
    if(done!=count){while(done){--done;Redirect(hooks[done].site,hooks[done].hook,g_base+hooks[done].target);}Log("REFUSED: could not install all hooks");return 1;}
#ifndef PAW_RECOVERY_INTEGRATED
    if(!InstallClientGuard()){while(done){--done;Redirect(hooks[done].site,hooks[done].hook,g_base+hooks[done].target);}Log("REFUSED: missing-client guard unavailable");return 1;}
    n=GetEnvironmentVariableW(L"PAWS_TEST_JOIN",join,80);
    if(n&&n<80&&(colon=wcsrchr(join,L':'))!=0){*colon=0;
        ((StringFn)(g_base+0x205de))(&g_joinIp,0,join);((StringFn)(g_base+0x205de))(&g_joinPort,0,colon+1);
        if(Redirect(0xef184,g_base+0x29fc02,(uintptr_t)IpHook)&&Redirect(0xef1a6,g_base+0x29fc02,(uintptr_t)PortHook))Log("private direct-IP test enabled");
    }
#endif
    g_active=1;Log("READY r2: 1.3.72 verified; native save completion; lobby recovery; auto_vote=%d; backup=2f4, disconnecting=2ed",g_autoVote);return 0;
}
EXPORT void __cdecl K2Plugin_Tick(void) {
    if(!g_active||GetCurrentThreadId()!=g_thread||InterlockedCompareExchange(&g_ticking,1,0))return;
    TickRecovery();InterlockedExchange(&g_ticking,0);
}

#ifdef PAW_RECOVERY_INTEGRATED
#include "integrated.inc"
#endif

#ifdef PAWS_TEST
static unsigned checks;
#define CHECK(x) do{++checks;if(!(x)){printf("FAIL line %d: %s\n",__LINE__,#x);exit(1);}}while(0)
static int createCalls,leaveCalls,requestCalls;static uintptr_t expectedSelf,expectedOther;static int expectedArg;
static void FC TestCreate(void *self,void *unused,int arg){(void)unused;CHECK((uintptr_t)self==expectedSelf);CHECK(arg==expectedArg);++createCalls;}
static void FC TestLeave(void *self,void *unused,void *other,int arg){(void)unused;CHECK((uintptr_t)self==expectedSelf);CHECK((uintptr_t)other==expectedOther);CHECK(arg==expectedArg);++leaveCalls;}
static void FC TestRequest(void *self,void *unused,void *other,int arg){(void)unused;CHECK((uintptr_t)self==expectedSelf);CHECK((uintptr_t)other==expectedOther);CHECK(arg==expectedArg);++requestCalls;}
/* A resource chain as the save task walks it: world -> made -> settings -> source. */
static unsigned char tWorld[0x40],tMade[0x90],tSettings[0x60],tSource[0x90],tSettings2[0x60],tOther[0x60];
static unsigned char __cdecl TestFromResource(void *p){return p==tSettings||p==tSettings2;}
static void Chain(void){
    memset(tWorld,0,sizeof(tWorld));memset(tMade,0,sizeof(tMade));memset(tSettings,0,sizeof(tSettings));memset(tSource,0,sizeof(tSource));memset(tSettings2,0,sizeof(tSettings2));
    g_fromResource=TestFromResource;*(uintptr_t*)(tWorld+0x28)=(uintptr_t)tMade;*(int*)(tMade+8)=0x10;
    *(uintptr_t*)(tMade+0x88)=(uintptr_t)tSettings;*(uintptr_t*)(tSettings+0x50)=(uintptr_t)tSource;
    *(int*)(tSource+8)=2;*(uintptr_t*)(tSource+0x88)=(uintptr_t)tOther; /* a random map's settings end the walk */
}
int main(void){
    unsigned i;Snapshot s;Bucket b;unsigned char admin[0x308]={0},conn[0x430]={0};
    InitializeCriticalSection(&g_lock);
    RememberIntent(11,21,31,100);CHECK(!ConsumeIntent(12,21,31,101));CHECK(!ConsumeIntent(11,22,31,101));CHECK(!ConsumeIntent(11,21,32,101));CHECK(ConsumeIntent(11,21,31,101));CHECK(!ConsumeIntent(11,21,31,102));
    RememberIntent(11,21,31,0xfffffff0);CHECK(ConsumeIntent(11,21,31,20));
    RememberIntent(11,21,31,100);CHECK(!ConsumeIntent(11,21,31,10100));
    for(i=1;i<=32;++i)RememberIntent(i,21,31,100+i);
    for(i=1;i<=32;++i)CHECK(ConsumeIntent(i,21,31,200));
    for(i=1;i<=33;++i)RememberIntent(i,21,31,100+i);
    CHECK(!ConsumeIntent(1,21,31,200));CHECK(ConsumeIntent(33,21,31,200));
    memset(&s,0,sizeof(s));CHECK(!RunningHost(&s));s.host=s.network=s.world=s.type=1;s.state=2;CHECK(RunningHost(&s));s.state=4;CHECK(!RunningHost(&s));s.state=1;CHECK(!RunningHost(&s));s.state=2;s.network=0;CHECK(!RunningHost(&s));
    {
        unsigned char session[0x140]={0},app[0x104]={0},type[16]={0};
        uintptr_t adminPtr=(uintptr_t)admin,sessionPtr=(uintptr_t)session;
        g_adminGlobal=(uintptr_t)&adminPtr;g_sessionGlobal=(uintptr_t)&sessionPtr;g_app=(uintptr_t)app;
        *(uintptr_t*)(admin+0x2e4)=adminPtr;*(int*)(session+0xf0)=2;session[0x100]=1;
        *(uintptr_t*)(app+0xfc)=(uintptr_t)type;*(int*)(type+0xc)=1;
        admin[0x2ed]=0;admin[0x2f4]=1;CHECK(SnapshotRead(&s));CHECK(s.backup&&!s.disconnecting&&s.host);
        admin[0x2ed]=1;admin[0x2f4]=0;CHECK(SnapshotRead(&s));CHECK(!s.backup&&s.disconnecting);CHECK(!RunningHost(&s));
        g_adminGlobal=g_sessionGlobal=g_app=0;
    }
    memset(&s,0,sizeof(s));s.session=10;s.world=20;s.network=s.type=s.host=1;s.state=2;s.backup=1;
    TrackRole(&s);CHECK(g_roleKnown&&g_originalHost);s.backup=0;TrackRole(&s);CHECK(g_originalHost);
    s.state=1;TrackRole(&s);CHECK(!g_roleKnown);s.state=2;s.host=0;TrackRole(&s);CHECK(g_roleKnown&&!g_originalHost);
    s.host=1;TrackRole(&s);CHECK(!g_originalHost);s.world=21;TrackRole(&s);CHECK(g_originalHost);
    s.network=0;TrackRole(&s);CHECK(!g_roleKnown);s.network=1;s.backup=1;
    CHECK(!VoteDue(&s,100,1));CHECK(!g_down);g_autoVote=1;
    CHECK(!VoteDue(&s,100,1));CHECK(!VoteDue(&s,20099,1));CHECK(VoteDue(&s,20100,1));
    g_votes=1;g_lastVote=20100;CHECK(!VoteDue(&s,50099,1));CHECK(VoteDue(&s,50100,1));
    g_votes=3;CHECK(!VoteDue(&s,100000,1));CHECK(!VoteDue(&s,100001,0));CHECK(!g_votes&&!g_down&&!g_lastVote);
    CHECK(!VoteDue(&s,110000,1));s.disconnecting=1;CHECK(!VoteDue(&s,140000,1));CHECK(!g_down);
    s.disconnecting=0;s.backup=0;CHECK(!VoteDue(&s,170000,1));s.backup=1;s.type=0;CHECK(!VoteDue(&s,180000,1));
    s.type=1;CHECK(!VoteDue(&s,190000,1));CHECK(VoteDue(&s,210000,1));g_autoVote=0;CHECK(!VoteDue(&s,210001,1));CHECK(!g_down);
    memset(admin,0,sizeof(admin));
    memset(&b,0,sizeof(b));b.tokens=1;CHECK(Take(&b,0,0));CHECK(Take(&b,0,1));CHECK(!Take(&b,0,1));CHECK(Take(&b,1,0));for(i=0;i<6;++i)CHECK(Take(&b,1,1));CHECK(!Take(&b,1,1));CHECK(Take(&b,0,1));CHECK(!Take(&b,0,1));
    CHECK(!Eligible((uintptr_t)admin,(uintptr_t)conn));*(uintptr_t*)(admin+0x2e4)=(uintptr_t)admin;*(int*)(conn+0x3c)=2;conn[0x98]=1;CHECK(Eligible((uintptr_t)admin,(uintptr_t)conn));
    admin[0x2ed]=1;CHECK(!Eligible((uintptr_t)admin,(uintptr_t)conn));admin[0x2ed]=0;admin[0x2ec]=1;CHECK(!Eligible((uintptr_t)admin,(uintptr_t)conn));admin[0x2ec]=0;conn[0x99]=1;CHECK(!Eligible((uintptr_t)admin,(uintptr_t)conn));conn[0x99]=0;*(int*)(conn+0x3c)=1;CHECK(!Eligible((uintptr_t)admin,(uintptr_t)conn));
    CHECK(!Readable(0,4));CHECK(!Readable((uintptr_t)-3,8));CHECK(!TextEqual(0,L"x"));CHECK(TextEqual((uintptr_t)L"abc",L"abc"));CHECK(!TextEqual((uintptr_t)L"abc",L"ab"));
    g_create=TestCreate;g_leave=TestLeave;g_request=TestRequest;expectedSelf=(uintptr_t)admin;expectedOther=(uintptr_t)conn;expectedArg=47;
    ((CreateFn)CreateHook)(admin,0,47);CHECK(createCalls==1);
    ((LeaveFn)LeaveHook)(admin,0,conn,47);CHECK(leaveCalls==1);CHECK(g_leaveCount==1);
    ((RequestFn)RequestHook)(admin,0,conn,47);CHECK(requestCalls==1);
    {
        unsigned char *image=(unsigned char*)VirtualAlloc(0,0x150000,MEM_COMMIT|MEM_RESERVE,PAGE_EXECUTE_READWRITE);
        unsigned char entry[]={0x8b,0x44,0x24,0x04,0x8b,0x40,0x0c,0x83,0x78,0x28,0,0x0f,0x95,0xc0,0x0f,0xb6,0xc0,0xc3};
        unsigned char node[16]={0},player[0x30]={0};int (__cdecl *count)(void*);
        CHECK(image!=0);g_base=(uintptr_t)image;memcpy(image+0x14c043,entry,sizeof(entry));count=(void*)(image+0x14c043);
        *(uintptr_t*)(node+12)=(uintptr_t)player;CHECK(count(node)==0);*(int*)(player+0x28)=1;CHECK(count(node)==1);
        CHECK(InstallClientGuard());CHECK(count(node)==1);*(int*)(player+0x28)=0;CHECK(count(node)==0);
        *(uintptr_t*)(node+12)=0;CHECK(count(node)==0);CHECK(count(0)==0);CHECK(!InstallClientGuard());
        VirtualFree(image,0,MEM_RELEASE);g_base=0;
    }
    /* Issue #10: a source without its header blocks the recovery save; nothing is queued. */
    Chain();CHECK(SaveSourcesReady((uintptr_t)tWorld));
    *(int*)(tSource+8)=0;CHECK(!SaveSourcesReady((uintptr_t)tWorld));
    Chain();*(int*)(tMade+8)=0;CHECK(SaveSourcesReady((uintptr_t)tWorld));
    Chain();*(uintptr_t*)(tMade+0x88)=(uintptr_t)tOther;CHECK(SaveSourcesReady((uintptr_t)tWorld));
    Chain();*(uintptr_t*)(tMade+0x88)=0;CHECK(!SaveSourcesReady((uintptr_t)tWorld));
    Chain();*(uintptr_t*)(tSettings+0x50)=0;CHECK(!SaveSourcesReady((uintptr_t)tWorld));
    Chain();*(uintptr_t*)(tSource+0x88)=(uintptr_t)tSettings2;*(uintptr_t*)(tSettings2+0x50)=(uintptr_t)tMade;*(int*)(tMade+8)|=2;
    CHECK(!SaveSourcesReady((uintptr_t)tWorld));
    Chain();*(uintptr_t*)(tWorld+0x28)=0;CHECK(!SaveSourcesReady((uintptr_t)tWorld));CHECK(!SaveSourcesReady(0));
    Chain();*(int*)(tSource+8)=0;memset(&s,0,sizeof(s));s.world=(uintptr_t)tWorld;g_step=IDLE;g_name[0]=0;
    {unsigned serial=g_serial;StartRecovery(&s,5);CHECK(g_step==IDLE&&g_serial==serial); /* no save name made, nothing queued */
     *(int*)(tSource+8)=2;StartRecovery(&s,5);CHECK(g_step==IDLE&&g_serial==serial+1);} /* control: a save is attempted (no console here) */
    printf("PASS %u native recovery checks (including x86 thiscall/fastcall hooks)\n",checks);return 0;
}
#endif
