/* External, x86, one-session crash recorder. Does not patch the game. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include "toolhelp.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdarg.h>
#include <wchar.h>
__declspec(dllimport) LPWSTR *WINAPI CommandLineToArgvW(LPCWSTR,int*);
__declspec(dllimport) BOOL WINAPI QueryFullProcessImageNameW(HANDLE,DWORD,LPWSTR,PDWORD);

typedef struct { DWORD ThreadId; EXCEPTION_POINTERS *ExceptionPointers; BOOL ClientPointers; } DumpException;
typedef BOOL (WINAPI *WriteDump)(HANDLE,DWORD,HANDLE,DWORD,DumpException*,void*,void*);
static FILE *record;
static WCHAR session[1024];
static FILETIME expectedStart;
static WCHAR expectedImage[1024];
#ifdef RECORDER_TEST
static int detachAfterArm=0;
#endif
static void logline(const char *fmt,...) {
    va_list args; SYSTEMTIME t; GetLocalTime(&t);
    if (!record) return;
    fprintf(record,"%02u:%02u:%02u ",t.wHour,t.wMinute,t.wSecond);
    va_start(args,fmt); vfprintf(record,fmt,args); va_end(args);
    fputc('\n',record); fflush(record);
}
static int error(const char *msg) {
    DWORD code=GetLastError();
    logline("ERROR %s win32=%lu",msg,code);
    return 1;
}
static int module_range(DWORD pid,const char *name,DWORD *low,DWORD *high,char *path) {
    HANDLE snapshot; MODULEENTRY32 m; int found=0;
    snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE|TH32CS_SNAPMODULE32,pid);
    if (snapshot==INVALID_HANDLE_VALUE) return 0;
    memset(&m,0,sizeof(m)); m.dwSize=sizeof(m);
    if (Module32First(snapshot,&m)) do {
        if (!lstrcmpiA(m.szModule,name)) {
            *low=(DWORD)m.modBaseAddr; *high=*low+m.modBaseSize;
            if(path) lstrcpynA(path,m.szExePath,MAX_PATH);
            found=1; break;
        }
    } while (Module32Next(snapshot,&m));
    CloseHandle(snapshot); return found;
}
static int capture(WriteDump writeDump,HANDLE process,DEBUG_EVENT *event) {
    HANDLE file,thread; CONTEXT context; EXCEPTION_RECORD exception;
    EXCEPTION_POINTERS pointers; DumpException info; WCHAR path[1040]; BOOL ok; DWORD saved;
    thread=OpenThread(THREAD_GET_CONTEXT|THREAD_QUERY_INFORMATION,FALSE,event->dwThreadId);
    if(!thread) return error("Cannot open the faulting thread");
    memset(&context,0,sizeof(context)); context.ContextFlags=CONTEXT_ALL;
    if(!GetThreadContext(thread,&context)) { CloseHandle(thread); return error("Cannot read exception context"); }
    CloseHandle(thread);
    exception=event->u.Exception.ExceptionRecord; exception.ExceptionRecord=NULL;
    pointers.ExceptionRecord=&exception; pointers.ContextRecord=&context;
    info.ThreadId=event->dwThreadId; info.ExceptionPointers=&pointers; info.ClientPointers=FALSE;
    swprintf(path,L"%ls.dmp",session);
    file=CreateFileW(path,GENERIC_WRITE,FILE_SHARE_READ,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);
    if(file==INVALID_HANDLE_VALUE) return error("Cannot create the full dump");
    logline("CAPTURE code=%08lX address=%p thread=%lu first_chance=%lu eip=%08lX edi=%08lX",
        exception.ExceptionCode,exception.ExceptionAddress,event->dwThreadId,event->u.Exception.dwFirstChance,context.Eip,context.Edi);
    /* Full memory + handles + unloaded modules + memory/thread info; tolerate inaccessible pages. */
    ok=writeDump(process,event->dwProcessId,file,0x21826,&info,NULL,NULL); saved=GetLastError();
    FlushFileBuffers(file); CloseHandle(file);
    logline("DUMP result=%u error=%08lX file=%ls",ok,ok?0:saved,path);
    return ok?0:1;
}
static int run(DWORD pid,const char *watch) {
    DWORD low=0,high=0,code; WCHAR path[1040]; char image[MAX_PATH],mutexName[100];
    HANDLE process,mutex; HMODULE dbghelp; WriteDump writeDump; DEBUG_EVENT event;
    FILETIME created,exitTime,kernel,user; DWORD imageLength=1024; WCHAR actualImage[1024];
    int initialBreak=1,captured=0,result=0,done=0;
    if(!pid) return 2;
    process=OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ|PROCESS_DUP_HANDLE|SYNCHRONIZE,FALSE,pid);
    if(!process) return error("Cannot open game process");
    /* Holding the handle plus creation-time/path checks prevents PID-reuse attachment. */
    if(!GetProcessTimes(process,&created,&exitTime,&kernel,&user) ||
       CompareFileTime(&created,&expectedStart)!=0 ||
       !QueryFullProcessImageNameW(process,0,actualImage,&imageLength) ||
       lstrcmpiW(actualImage,expectedImage) || WaitForSingleObject(process,0)!=WAIT_TIMEOUT) {
        CloseHandle(process); return error("Game identity changed; not attaching");
    }
    snprintf(mutexName,sizeof(mutexName),"Local\\PawsGraphicsRecorder-%lu",pid);
    mutex=CreateMutexA(NULL,FALSE,mutexName);
    if(!mutex || GetLastError()==ERROR_ALREADY_EXISTS) return error("Diagnostics already attached to this game");
    swprintf(path,L"%ls.log",session); record=_wfopen(path,L"wb");
    if(!record) {CloseHandle(mutex); CloseHandle(process); return error("Cannot open diagnostic log");}
    logline("START r3 pid=%lu watch=%s recorder=%lu",pid,watch,GetCurrentProcessId());
    if(module_range(pid,watch,&low,&high,image))
        logline("MODULE %s base=%08lX end=%08lX",watch,low,high);
    GetSystemDirectoryW(path,1024); lstrcatW(path,L"\\dbghelp.dll");
    dbghelp=LoadLibraryW(path);
    writeDump=dbghelp?(WriteDump)GetProcAddress(dbghelp,"MiniDumpWriteDump"):NULL;
    if(!writeDump) return error("System MiniDumpWriteDump is unavailable");
    if(!DebugActiveProcess(pid)) {CloseHandle(process); return error("Cannot attach diagnostics");}
    if(!DebugSetProcessKillOnExit(FALSE)) {DebugActiveProcessStop(pid); CloseHandle(process); return error("Cannot enable safe debugger exit");}
    logline("ATTACHED kill_on_recorder_exit=false");
    while(!done) {
        if(!WaitForDebugEvent(&event,1000)) {
            DWORD waitError=GetLastError();
            DWORD state=WaitForSingleObject(process,0);
            /* Some game shutdown paths leave the debug queue without an
             * EXIT_PROCESS event. The verified handle remains bound to this
             * exact process even if its PID is reused. Only check after an
             * empty/failed wait: pending exceptions must still be continued. */
            if(state==WAIT_OBJECT_0) {
                DWORD exitCode=0;
                if(GetExitCodeProcess(process,&exitCode))
                    logline("TARGET_EXIT_HANDLE code=%lu captured=%d",exitCode,captured);
                else logline("TARGET_EXIT_HANDLE captured=%d code_unavailable=%lu",captured,GetLastError());
                break;
            }
            if(state==WAIT_FAILED) {result=error("Cannot wait for game exit"); break;}
            if(waitError==ERROR_SEM_TIMEOUT) continue;
            logline("WAIT_FAILED error=%lu",waitError); result=1; break;
        }
        code=DBG_CONTINUE;
        if(event.dwDebugEventCode==EXCEPTION_DEBUG_EVENT) {
            DWORD address=(DWORD)event.u.Exception.ExceptionRecord.ExceptionAddress;
            DWORD kind=event.u.Exception.ExceptionRecord.ExceptionCode;
            int graphicsFault=0;
            /* A bad virtual call can land outside every module (log-70: 0x62).
             * Capture before the game's fatal dialog/unwind loses the heap and
             * caller context. Do not capture read faults in our safe probes.
             * This records the fault; DBG_EXCEPTION_NOT_HANDLED is unchanged. */
            int invalidCall=kind==EXCEPTION_ACCESS_VIOLATION &&
                event.u.Exception.ExceptionRecord.NumberParameters>=2 &&
                event.u.Exception.ExceptionRecord.ExceptionInformation[0]==8;
            if(kind==EXCEPTION_ACCESS_VIOLATION) {
                const char *vendors[]={watch,"atidxx32.dll","atiumdag.dll","igdumdim32.dll","igd10iumd32.dll"};
                unsigned int i;
                for(i=0;i<sizeof(vendors)/sizeof(vendors[0]);i++) {
                    if(module_range(pid,vendors[i],&low,&high,NULL) && address>=low && address<high) {
                        graphicsFault=1; logline("FAULT_MODULE %s base=%08lX",vendors[i],low); break;
                    }
                }
            }
            code=DBG_EXCEPTION_NOT_HANDLED;
            if(initialBreak && kind==EXCEPTION_BREAKPOINT) {
                initialBreak=0; code=DBG_CONTINUE; logline("ARMED");
            } else if(!captured && (graphicsFault || invalidCall || !event.u.Exception.dwFirstChance)) {
                if(invalidCall) logline("INVALID_CALL address=%08lX",address);
                result=capture(writeDump,process,&event); captured=1;
            }
        } else if(event.dwDebugEventCode==LOAD_DLL_DEBUG_EVENT) {
            if(event.u.LoadDll.hFile) CloseHandle(event.u.LoadDll.hFile);
        } else if(event.dwDebugEventCode==CREATE_PROCESS_DEBUG_EVENT) {
            if(event.u.CreateProcessInfo.hFile) CloseHandle(event.u.CreateProcessInfo.hFile);
        } else if(event.dwDebugEventCode==EXIT_PROCESS_DEBUG_EVENT) {
            logline("TARGET_EXIT code=%lu captured=%d",event.u.ExitProcess.dwExitCode,captured); done=1;
        }
        if(!ContinueDebugEvent(event.dwProcessId,event.dwThreadId,code)) {
            logline("CONTINUE_FAILED error=%lu",GetLastError()); result=1; break;
        }
#ifdef RECORDER_TEST
        /* Reproduce a lost debug-event stream while the target stays alive.
         * Production builds cannot request this fault injection. */
        if(detachAfterArm && !initialBreak && !done) {
            if(!DebugActiveProcessStop(pid)) {result=error("Test detach failed"); break;}
            logline("TEST_DETACHED"); detachAfterArm=0;
        }
#endif
        if(captured && !done) {
            BOOL detached=DebugActiveProcessStop(pid);
            logline("DETACH result=%u error=%lu",detached,detached?0:GetLastError());
            if(detached) done=1;
            /* If detachment races with process exit, keep draining events. */
        }
    }
    DebugActiveProcessStop(pid); CloseHandle(process); CloseHandle(mutex); FreeLibrary(dbghelp);
    logline("RECORDER_EXIT result=%d",result); fclose(record); record=NULL; return result;
}
int WINAPI WinMain(HINSTANCE instance,HINSTANCE previous,LPSTR command,int show) {
    int argc,result; LPWSTR *argv; ULARGE_INTEGER stamp; char watch[128]="nvd3dum.dll";
    (void)instance;(void)previous;(void)command;(void)show;
    argv=CommandLineToArgvW(GetCommandLineW(),&argc);
    if(!argv || argc<5 || argc>7) return 2;
    if(wcslen(argv[3])>=1024 || wcslen(argv[4])>=1024) {LocalFree(argv);return 2;}
    stamp.QuadPart=_wcstoui64(argv[2],NULL,16);
    expectedStart.dwLowDateTime=stamp.LowPart;expectedStart.dwHighDateTime=stamp.HighPart;
    lstrcpyW(expectedImage,argv[3]);lstrcpyW(session,argv[4]);
#ifdef RECORDER_TEST
    if(argc>=6) {
        unsigned int i; for(i=0;i<sizeof(watch)-1 && argv[5][i];i++) watch[i]=(char)argv[5][i];
        watch[i]=0;
    }
    if(argc==7) {
        if(lstrcmpW(argv[6],L"detach-after-arm")) {LocalFree(argv);return 2;}
        detachAfterArm=1;
    }
#else
    if(argc!=5) {LocalFree(argv);return 2;}
#endif
    result=run(wcstoul(argv[1],NULL,10),watch);LocalFree(argv);return result;
}
