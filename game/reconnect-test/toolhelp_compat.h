#ifndef PAW_TOOLHELP_COMPAT_H
#define PAW_TOOLHELP_COMPAT_H
/* The pinned TinyCC bundle lacks tlhelp32.h. ANSI Win32 declarations from:
 * https://learn.microsoft.com/windows/win32/api/tlhelp32/ns-tlhelp32-moduleentry32 */
#include <windows.h>
#define TH32CS_SNAPMODULE 0x00000008
typedef struct {
    DWORD dwSize,th32ModuleID,th32ProcessID,GlblcntUsage,ProccntUsage;
    BYTE *modBaseAddr;
    DWORD modBaseSize;
    HMODULE hModule;
    char szModule[256],szExePath[MAX_PATH];
} PawModuleEntry32;
WINBASEAPI HANDLE WINAPI CreateToolhelp32Snapshot(DWORD,DWORD);
WINBASEAPI BOOL WINAPI Module32First(HANDLE,PawModuleEntry32*);
WINBASEAPI BOOL WINAPI Module32Next(HANDLE,PawModuleEntry32*);
typedef char PawToolhelpX86Abi[(sizeof(PawModuleEntry32)==548)?1:-1];
#endif
