/* Shared with optional save modules. This is a main-thread scheduling signal,
 * not a mutex for concurrent producers. Each module writes only its own flag.
 * See docs/recovery-save-coordination.md for lifecycle and failure semantics. */
#include "toolhelp_compat.h"
EXPORT volatile LONG PawSaveBusy = 0;

static int ModuleSaveBusy(HMODULE module) {
    volatile LONG *flag=(volatile LONG*)GetProcAddress(module,"PawSaveBusy");
    MEMORY_BASIC_INFORMATION m;
    if(!flag||flag==&PawSaveBusy)return 0;
    /* Reject a function/forwarder/unreadable/misaligned export. Never execute it
     * or write another module's flag. The module is referenced by the caller. */
    if(((uintptr_t)flag&3)||!VirtualQuery((const void*)flag,&m,sizeof(m))||
       m.State!=MEM_COMMIT||m.Type!=MEM_IMAGE||m.AllocationBase!=(void*)module||
       (m.Protect&(PAGE_GUARD|PAGE_NOACCESS))||!(m.Protect&(PAGE_READWRITE|PAGE_WRITECOPY))||
       (uintptr_t)flag+sizeof(LONG)>(uintptr_t)m.BaseAddress+m.RegionSize)return 1;
    return *flag!=0;
}
static int OtherSaveBusy(void) {
    HANDLE snapshot;PawModuleEntry32 entry;int busy=0;DWORD error;
    snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,GetCurrentProcessId());
    if(snapshot==INVALID_HANDLE_VALUE)return 1;
    memset(&entry,0,sizeof(entry));entry.dwSize=sizeof(entry);
    if(!Module32First(snapshot,&entry)){CloseHandle(snapshot);return 1;}
    do {
        HMODULE held=0;
        if(!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,(LPCSTR)entry.modBaseAddr,&held)){
            busy=1;break; /* retry on a later tick after a concurrent unload */
        }
        busy=held!=entry.hModule||ModuleSaveBusy(held);
        FreeLibrary(held);
        if(busy)break;
    }while(Module32Next(snapshot,&entry));
    error=GetLastError();CloseHandle(snapshot);
    return busy||error!=ERROR_NO_MORE_FILES;
}
