/* Exercise the real integrated commit with locks a paused game thread may own.
 * A synthetic hash-guarded image is used; no running game is touched. */
#define PAW_RECOVERY_INTEGRATED
#include "reconnect.c"
static HANDLE ready,go;
static PawRecoveryConfig config;
static uintptr_t testImage;
static volatile LONG done;
static DWORD prepared,result;
static DWORD WINAPI InstallWorker(void *unused) {
    (void)unused;prepared=PawRecoveryPrepare(&config);
    SetEvent(ready);WaitForSingleObject(go,INFINITE);
    result=prepared?prepared:PawRecoveryInstall(&config,testImage);
    InterlockedExchange(&done,1);return result;
}
typedef LONG (WINAPI *LoaderLock)(ULONG,ULONG*,ULONG_PTR*);
typedef LONG (WINAPI *LoaderUnlock)(ULONG,ULONG_PTR);
typedef void (WINAPI *PebLock)(void);
#define CHECK(x) do{if(!(x)){printf("FAIL install lock line %d\n",__LINE__);return 1;}}while(0)
int main(int argc,char **argv) {
    unsigned char *image=VirtualAlloc(0,0x700000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    IMAGE_DOS_HEADER *dos=(void*)image;IMAGE_NT_HEADERS32 *nt=(void*)(image+128);
    IMAGE_IMPORT_DESCRIPTOR *imports=(void*)(image+1024);DWORD *names=(void*)(image+2048),*slots=(void*)(image+3072);
    HMODULE ntdll=GetModuleHandleA("ntdll.dll");
    LoaderLock lock=(LoaderLock)GetProcAddress(ntdll,"LdrLockLoaderLock");
    LoaderUnlock unlock=(LoaderUnlock)GetProcAddress(ntdll,"LdrUnlockLoaderLock");
    PebLock acquire=(PebLock)GetProcAddress(ntdll,"RtlAcquirePebLock"),release=(PebLock)GetProcAddress(ntdll,"RtlReleasePebLock");
    HANDLE worker;unsigned i,j;DWORD start,expected=0;LONG finished;ULONG disposition;ULONG_PTR cookie=0;
    CHECK(argc==2&&image&&lock&&unlock&&acquire&&release);
    CHECK(PawRecoveryPrepare(0)==60);CHECK(PawRecoveryInstall(0,0)==68);
    dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=128;nt->Signature=IMAGE_NT_SIGNATURE;nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR32_MAGIC;
    nt->OptionalHeader.SizeOfImage=0x700000;nt->OptionalHeader.DataDirectory[1].VirtualAddress=1024;
    imports->Name=4096;imports->OriginalFirstThunk=2048;imports->FirstThunk=3072;
    names[0]=5000;memcpy(image+5002,"QueryPerformanceCounter",24);
    slots[0]=(DWORD)GetProcAddress(GetModuleHandleA("kernel32.dll"),"QueryPerformanceCounter");
    for(i=0;i<sizeof(guards)/sizeof(guards[0]);++i){const Guard *g=guards+i;
        memcpy(image+g->rva,g->bytes,g->size);
        for(j=0;j<g->count;++j)*(DWORD*)(image+g->rva+g->fixups[j])+=(DWORD)image-0x460000;
    }
    testImage=(uintptr_t)image;config.mainThread=GetCurrentThreadId();
    CHECK(PawRecoveryPrepare(&config)==61);
    CHECK(GetCurrentDirectoryW(MAX_PATH,config.data)>0);CHECK(PawRecoveryPrepare(&config)==62);
    wcscpy(config.log,config.data);wcscat(config.log,L"\\install-lock-test.log");
    ready=CreateEventW(0,TRUE,FALSE,0);go=CreateEventW(0,TRUE,FALSE,0);
    CHECK(ready&&go);worker=CreateThread(0,0,InstallWorker,0,0,0);CHECK(worker);
    CHECK(WaitForSingleObject(ready,5000)==WAIT_OBJECT_0&&prepared==0);
    CHECK(PawRecoveryPrepare(&config)==60);
    if(!strcmp(argv[1],"heap-guard")){image[0x1503ea]=0;expected=66;}
    if(!strcmp(argv[1],"heap-counter")){slots[0]=0;expected=64;}
    if(!strcmp(argv[1],"heap-config")){config.autoVote=1;expected=68;}
    /* Do not CHECK/printf while owning these locks. Release them even if the
     * old installer blocks, then join and fail cleanly instead of hanging QA. */
    if(!strcmp(argv[1],"loader"))lock(0,&disposition,&cookie);
    else if(!strcmp(argv[1],"peb"))acquire();
    else if(!strncmp(argv[1],"heap",4))HeapLock(GetProcessHeap());
    start=GetTickCount();SetEvent(go);
    while(!done&&GetTickCount()-start<2000)Sleep(1);
    finished=done; /* DLL_THREAD_DETACH may still wait for the loader lock. */
    if(!strcmp(argv[1],"loader"))unlock(0,cookie);
    else if(!strcmp(argv[1],"peb"))release();
    else if(!strncmp(argv[1],"heap",4))HeapUnlock(GetProcessHeap());
    CHECK(WaitForSingleObject(worker,5000)==WAIT_OBJECT_0);CHECK(finished&&result==expected);
    CHECK(expected?(!g_active&&!recoveryAnnounce):(g_active&&recoveryAnnounce&&!g_installQuiet));
    if(!expected){
        /* The first main-thread tick emits the deferred status after resume,
         * keeps the original counter callable and preserves Win32 last error. */
        LARGE_INTEGER value;g_active=0;recoveryLastTick=GetTickCount()-50;
        SetLastError(1234);CHECK(RecoveryCounter(&value));CHECK(GetLastError()==1234&&!recoveryAnnounce);
        CHECK(GetFileAttributesW(config.log)!=INVALID_FILE_ATTRIBUTES);
    }
    CloseHandle(worker);CloseHandle(ready);CloseHandle(go);
    DeleteCriticalSection(&g_lock);VirtualFree(image,0,MEM_RELEASE);
    printf("PASS install commit with %s held; result=%lu\n",argv[1],result);return 0;
}
