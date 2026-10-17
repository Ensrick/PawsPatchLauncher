#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
static char marker[MAX_PATH];
static int handled=0;
static LONG WINAPI handler(EXCEPTION_POINTERS *e) {
    if(e->ExceptionRecord->ExceptionCode==EXCEPTION_ACCESS_VIOLATION) {
        handled++;
        if(e->ExceptionRecord->NumberParameters>=2 && e->ExceptionRecord->ExceptionInformation[0]==8) {
            /* Fixture-only recovery from the deliberate invalid call. */
            e->ContextRecord->Eip=*(DWORD*)e->ContextRecord->Esp;
            e->ContextRecord->Esp+=4;
            return EXCEPTION_CONTINUE_EXECUTION;
        }
        /* Deliberate "mov eax,[eax]" in fault(); resume after its two bytes. */
        e->ContextRecord->Eip+=2;
        return EXCEPTION_CONTINUE_EXECUTION;
    }
    return EXCEPTION_CONTINUE_SEARCH;
}
static LONG WINAPI unhandled(EXCEPTION_POINTERS *e) {
    FILE *f=fopen(marker,"wb"); (void)e;
    if(f) {fprintf(f,"unhandled-filter\n"); fclose(f);}
    ExitProcess(77); return EXCEPTION_EXECUTE_HANDLER;
}
static void fault(void) {
    __asm__ __volatile__("xor %%eax,%%eax; mov (%%eax),%%eax" ::: "eax");
}
int main(int argc,char **argv) {
    FILE *f; void *heap;
    if(argc<4) return 2;
    lstrcpynA(marker,argv[2],MAX_PATH);
    if(!strcmp(argv[1],"handled") || !strcmp(argv[1],"invalid-call")) AddVectoredExceptionHandler(1,handler);
    if(!strcmp(argv[1],"filter")) SetUnhandledExceptionFilter(unhandled);
    SetErrorMode(SEM_NOGPFAULTERRORBOX|SEM_FAILCRITICALERRORS);
    heap=VirtualAlloc(NULL,1024*1024,MEM_COMMIT|MEM_RESERVE,PAGE_READWRITE);
    if(!heap) return 3;
    memset(heap,0x63,1024*1024); strcpy((char*)heap,"PAWS_FULL_HEAP_EVIDENCE_20260920");
    Sleep(strtoul(argv[3],NULL,10));
    if(!strcmp(argv[1],"invalid-call")) ((void(*)(void))0x62)();
    else if(strcmp(argv[1],"normal")) fault();
    f=fopen(marker,"wb"); if(!f) return 4;
    fprintf(f,"normal-exit handled=%d heap=%p\n",handled,heap); fclose(f);
    VirtualFree(heap,0,MEM_RELEASE); return 0;
}
