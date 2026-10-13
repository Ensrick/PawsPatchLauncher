/* QA only: runs a fixed native command script and fills the Direct IP dialog.
 * No recovery, transfer, save, vote, role or simulation hooks. Not shipped. */
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <wchar.h>
#define FC __attribute__((fastcall))
typedef struct {unsigned api;uintptr_t exeBase;const wchar_t *gameDir,*modsDir;void (__cdecl *log)(const char*,const char*,...);} Host;
static uintptr_t base,consoleGlobal;static DWORD thread,lastPoll;static wchar_t *ip,*port;static int sent,inTick;
static wchar_t requestPath[MAX_PATH];static unsigned lastRequest;
static void (__cdecl *writeLog)(const char*,const char*,...);
typedef void*(FC *StringFn)(void*,void*,const wchar_t*);
typedef void(FC *RunFn)(void*,void*,void*,int);
typedef void(FC *ReleaseFn)(void*,void*);
static void *FC Ip(void *w,void *unused){(void)w;(void)unused;return &ip;}
static void *FC Port(void *w,void *unused){(void)w;(void)unused;return &port;}
static int Redirect(unsigned rva,void *fn){DWORD old,ignored;uintptr_t site=base+rva;int rel=(uintptr_t)fn-site-5;
 if(*(unsigned char*)site!=0xe8||site+5+*(int*)(site+1)!=base+0x29fc02)return 0;
 if(!VirtualProtect((void*)site,5,PAGE_EXECUTE_READWRITE,&old))return 0;
 memcpy((void*)(site+1),&rel,4);VirtualProtect((void*)site,5,old,&ignored);FlushInstructionCache(GetCurrentProcess(),(void*)site,5);return 1;}
__declspec(dllexport) int __cdecl K2Plugin_Start(const Host *host){wchar_t join[80],*colon;unsigned n;
 if(!host||host->api!=1)return 1;base=host->exeBase;thread=GetCurrentThreadId();writeLog=host->log;
 {wchar_t marker[MAX_PATH];
  if(wcslen(host->gameDir)>MAX_PATH-32)return 1;
  swprintf(marker,L"%ls\\reconnect-test.marker",host->gameDir);
  if(GetFileAttributesW(marker)==INVALID_FILE_ATTRIBUTES)return 1;
  swprintf(requestPath,L"%ls\\Mods\\rig-command.txt",host->gameDir);
 }
 {wchar_t flag[4];if(GetEnvironmentVariableW(L"PAWS_RIG_EXTERNAL_SCRIPT",flag,4)==1&&flag[0]==L'1')sent=1;}
 if(*(unsigned short*)(base+0x13df06)!=0x0d8b)return 1;
 consoleGlobal=*(uintptr_t*)(base+0x13df08);
 n=GetEnvironmentVariableW(L"PAWS_TEST_JOIN",join,80);
 if(n&&n<80&&(colon=wcsrchr(join,L':'))){*colon=0;
  ((StringFn)(base+0x205de))(&ip,0,join);((StringFn)(base+0x205de))(&port,0,colon+1);
  if(!Redirect(0xef184,Ip)||!Redirect(0xef1a6,Port))return 1;
 }
 host->log("rig-input","QA input only; no recovery hooks");return 0;
}
static void Run(uintptr_t console,const wchar_t *command){wchar_t *text=0;
 ((StringFn)(base+0x205de))(&text,0,command);
 if(text){((RunFn)(base+0x1f9963))((void*)console,0,&text,0);
 ((ReleaseFn)(base+0x21375))((char*)text-16,0);}
}
/* Test controller requests ordinary engine scripts on the main thread. It does
 * not edit session, save-task, recovery, connection or simulation state. */
__declspec(dllexport) void __cdecl K2Plugin_Tick(void){uintptr_t console;DWORD now,n;HANDLE f;char data[32];unsigned id;wchar_t command[80];
 if(inTick||GetCurrentThreadId()!=thread||!(console=*(uintptr_t*)consoleGlobal))return;
 inTick=1;
 if(!sent){sent=1;Run(console,L"Exec Mods/rig.txt");}
 now=GetTickCount();if(now-lastPoll<100){inTick=0;return;}lastPoll=now;
 f=CreateFileW(requestPath,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE|FILE_SHARE_DELETE,0,OPEN_EXISTING,0,0);
 if(f!=INVALID_HANDLE_VALUE){
  if(ReadFile(f,data,sizeof(data)-1,&n,0)){data[n]=0;id=(unsigned)strtoul(data,0,10);
   if(id>lastRequest&&id<100000){lastRequest=id;swprintf(command,L"Exec Mods/rig-request-%u.txt",id);
    writeLog("rig-input","dispatch request %u",id);Run(console,command);writeLog("rig-input","queued request %u",id);}
  }CloseHandle(f);
 }inTick=0;
}
