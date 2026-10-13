/* Executable ABI/import/thread tests for the shipping adapter, no game needed. */
#define PAW_RECOVERY_INTEGRATED
#include "reconnect.c"
static unsigned checks,calls;
#define CHECK(x) do {++checks;if(!(x)){printf("FAIL integrated line %d: %s\n",__LINE__,#x);return 1;}}while(0)
static BOOL WINAPI CounterFixture(LARGE_INTEGER *value){++calls;value->QuadPart=123456;return GetLastError()==789;}
static DWORD WINAPI OtherThread(void *arg){LARGE_INTEGER value;(void)arg;SetLastError(789);RecoveryCounter(&value);return 0;}
int main(void){
    unsigned char *image=VirtualAlloc(0,0x700000,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    IMAGE_DOS_HEADER *dos=(void*)image;IMAGE_NT_HEADERS32 *nt=(void*)(image+128);
    IMAGE_IMPORT_DESCRIPTOR *imports=(void*)(image+1024);DWORD *names=(void*)(image+2048),*slots=(void*)(image+3072);
    unsigned i,j;LARGE_INTEGER value;HANDLE thread;DWORD id;
    CHECK(image!=0);CHECK(sizeof(PawRecoveryConfig)==1048);CHECK(!CounterImport(0));CHECK(!CounterImport((uintptr_t)image));
    dos->e_magic=IMAGE_DOS_SIGNATURE;dos->e_lfanew=128;nt->Signature=IMAGE_NT_SIGNATURE;nt->OptionalHeader.Magic=IMAGE_NT_OPTIONAL_HDR32_MAGIC;
    nt->OptionalHeader.SizeOfImage=0x700000;nt->OptionalHeader.DataDirectory[1].VirtualAddress=1024;
    imports->Name=4096;imports->OriginalFirstThunk=2048;imports->FirstThunk=3072;
    names[0]=5000;memcpy(image+5002,"QueryPerformanceCounter",24);
    CHECK(CounterImport((uintptr_t)image)==(uintptr_t*)slots);
    names[0]=IMAGE_ORDINAL_FLAG32|12;CHECK(!CounterImport((uintptr_t)image));names[0]=5000;
    image[5002]='X';CHECK(!CounterImport((uintptr_t)image));image[5002]='Q';
    CHECK(!ExecutableCounter(0));CHECK(!ExecutableCounter((uintptr_t)image));CHECK(!ExecutableCounter((uintptr_t)RecoveryCounter));
    CHECK(ExecutableCounter((uintptr_t)GetProcAddress(GetModuleHandleA("kernel32.dll"),"QueryPerformanceCounter")));
    CHECK(ExecutableCounter((uintptr_t)CounterFixture));
    originalCounter=CounterFixture;g_thread=GetCurrentThreadId();g_active=0;recoveryLastTick=GetTickCount();
    SetLastError(789);CHECK(RecoveryCounter(&value));CHECK(value.QuadPart==123456&&calls==1);
    recoveryLastTick=123;thread=CreateThread(0,0,OtherThread,0,0,&id);CHECK(thread!=0);CHECK(WaitForSingleObject(thread,5000)==0);CloseHandle(thread);
    CHECK(recoveryLastTick==123&&calls==2);
    /* Relocated stock guards pass. R2-owned sites can differ, recovery's cannot. */
    for(i=0;i<sizeof(guards)/sizeof(guards[0]);++i){const Guard *g=guards+i;
        memcpy(image+g->rva,g->bytes,g->size);for(j=0;j<g->count;++j)*(DWORD*)(image+g->rva+g->fixups[j])+=(DWORD)image-0x460000;
    }
    CHECK(Validate((uintptr_t)image));image[0x14c047]=0xe9;image[0x150273]=0;CHECK(Validate((uintptr_t)image));
    image[0x1503ea]=0;CHECK(!Validate((uintptr_t)image));
    VirtualFree(image,0,MEM_RELEASE);printf("PASS %u integrated recovery checks\n",checks);return 0;
}
