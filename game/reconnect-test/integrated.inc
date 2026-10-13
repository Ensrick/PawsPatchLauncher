#include "integrated.h"
static Host recoveryHost;
static wchar_t recoveryLog[MAX_PATH];
static BOOL (WINAPI *originalCounter)(LARGE_INTEGER*);
static DWORD recoveryLastTick;
static void __cdecl RecoveryLog(const char *category,const char *format,...) {
    char message[1400],body[1024];DWORD written;SYSTEMTIME t;va_list args;
    HANDLE file;int n;(void)category;
    va_start(args,format);vsnprintf(body,sizeof(body),format,args);va_end(args);body[sizeof(body)-1]=0;
    GetLocalTime(&t);n=snprintf(message,sizeof(message),"[%02u:%02u:%02u.%03u] paws-recovery: %s\r\n",t.wHour,t.wMinute,t.wSecond,t.wMilliseconds,body);
    if(n<=0||n>=(int)sizeof(message))return;
    file=CreateFileW(recoveryLog,FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,0,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,0);
    if(file==INVALID_HANDLE_VALUE)return;WriteFile(file,message,n,&written,0);CloseHandle(file);
}
static BOOL WINAPI RecoveryCounter(LARGE_INTEGER *value) {
    DWORD error=GetLastError(),now=GetTickCount();
    if(GetCurrentThreadId()==g_thread&&now-recoveryLastTick>=50){recoveryLastTick=now;K2Plugin_Tick();}
    SetLastError(error);return originalCounter(value);
}
static int ExecutableCounter(uintptr_t address) {
    MEMORY_BASIC_INFORMATION m;
    if(!address||address==(uintptr_t)RecoveryCounter||!VirtualQuery((void*)address,&m,sizeof(m)))return 0;
    return m.State==MEM_COMMIT&&m.Type==MEM_IMAGE&&!(m.Protect&(PAGE_GUARD|PAGE_NOACCESS))&&
        (m.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY));
}
/* Only the game's own named PE32 import. Preserve the existing callable target,
 * including Windows compatibility/overlay wrappers, rather than bypassing it.
 * Installed by the normal helper's suspended-thread transaction, not DllMain. */
static uintptr_t *CounterImport(uintptr_t base) {
    IMAGE_DOS_HEADER *dos=(IMAGE_DOS_HEADER*)base;IMAGE_NT_HEADERS32 *nt;
    IMAGE_IMPORT_DESCRIPTOR *entry;unsigned n,i;DWORD limit;
    if(!Readable(base,sizeof(*dos))||dos->e_magic!=IMAGE_DOS_SIGNATURE||dos->e_lfanew<0||dos->e_lfanew>0x100000)return 0;
    nt=(IMAGE_NT_HEADERS32*)(base+dos->e_lfanew);
    if(!Readable((uintptr_t)nt,sizeof(*nt))||nt->Signature!=IMAGE_NT_SIGNATURE||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR32_MAGIC)return 0;
    limit=nt->OptionalHeader.SizeOfImage;
    if(!limit||limit>0x10000000||nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress>=limit)return 0;
    entry=(IMAGE_IMPORT_DESCRIPTOR*)(base+nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress);
    for(n=0;n<512;++n,++entry){
        IMAGE_THUNK_DATA32 *names;uintptr_t *slots;
        if(!Readable((uintptr_t)entry,sizeof(*entry))||!entry->Name)return 0;
        if(!entry->OriginalFirstThunk||entry->OriginalFirstThunk>=limit||entry->FirstThunk>=limit)continue;
        names=(IMAGE_THUNK_DATA32*)(base+entry->OriginalFirstThunk);slots=(uintptr_t*)(base+entry->FirstThunk);
        for(i=0;i<4096;++i){
            uintptr_t rva;
            if(!Readable((uintptr_t)(names+i),4)||!Readable((uintptr_t)(slots+i),4))return 0;
            rva=names[i].u1.AddressOfData;if(!rva)break;
            if((rva&IMAGE_ORDINAL_FLAG32)||rva>=limit)continue;
            if(Readable(base+rva+2,24)&&!memcmp((void*)(base+rva+2),"QueryPerformanceCounter",24))return slots+i;
        }
    }return 0;
}
DWORD PawRecoveryInstall(const PawRecoveryConfig *config,uintptr_t image) {
    uintptr_t *counter;DWORD old,ignored;unsigned n;
    if(!config||!config->mainThread||config->autoVote>1||g_active)return 60;
    for(n=0;n<MAX_PATH&&config->data[n];++n){}if(!n||n>=MAX_PATH-130)return 61;
    for(n=0;n<MAX_PATH&&config->log[n];++n){}if(!n||n==MAX_PATH)return 62;
    if(GetFileAttributesW(config->data)==INVALID_FILE_ATTRIBUTES)return 63;
    memcpy(recoveryLog,config->log,sizeof(recoveryLog));
    counter=CounterImport(image);if(!counter||!ExecutableCounter(*counter)){
        RecoveryLog("install","REFUSED counter import: slot=%p actual=%p image=%p",counter,counter?*counter:0,image);return 64;}
    originalCounter=(void*)*counter;
    if(!VirtualProtect(counter,4,PAGE_READWRITE,&old))return 65;
    memcpy(g_data,config->data,sizeof(g_data));memcpy(recoveryLog,config->log,sizeof(recoveryLog));
    g_thread=config->mainThread;g_autoVote=config->autoVote;
    recoveryHost.api=1;recoveryHost.exeBase=image;recoveryHost.log=RecoveryLog;
    if(K2Plugin_Start(&recoveryHost)){VirtualProtect(counter,4,old,&ignored);return 66;}
    InterlockedExchange((LONG*)counter,(LONG)RecoveryCounter);
    if(!VirtualProtect(counter,4,old,&ignored)){g_active=0;return 67;}
    Log("INTEGRATED: Pure helper owns R2 and null-player guard; main_thread=%lu",g_thread);
    return 0;
}
