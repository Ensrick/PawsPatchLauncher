/* Private Documents for this opt-in test only. Based on Ensrick's MIT rig_shfolder.cpp. */
#include <windows.h>
#include <wchar.h>
static HMODULE real_dll(void){
    static HMODULE real;wchar_t path[MAX_PATH];
    if(!real){unsigned n=GetSystemDirectoryW(path,MAX_PATH);if(!n||n>MAX_PATH-16)return 0;wcscat(path,L"\\shfolder.dll");real=LoadLibraryW(path);}return real;
}
__declspec(dllexport) HRESULT WINAPI SHGetFolderPathW(HWND w,int id,HANDLE token,DWORD flags,LPWSTR path){
    wchar_t target[MAX_PATH];DWORD n=GetEnvironmentVariableW(L"PAWS_TEST_DOCUMENTS",target,MAX_PATH);
    typedef HRESULT(WINAPI*Fn)(HWND,int,HANDLE,DWORD,LPWSTR);Fn fn;
    if(path&&n&&n<MAX_PATH&&(id&255)==5){wcscpy(path,target);return S_OK;}
    fn=(Fn)GetProcAddress(real_dll(),"SHGetFolderPathW");return fn?fn(w,id,token,flags,path):E_FAIL;
}
__declspec(dllexport) HRESULT WINAPI SHGetFolderPathA(HWND w,int id,HANDLE token,DWORD flags,LPSTR path){
    char target[MAX_PATH];DWORD n=GetEnvironmentVariableA("PAWS_TEST_DOCUMENTS",target,MAX_PATH);
    typedef HRESULT(WINAPI*Fn)(HWND,int,HANDLE,DWORD,LPSTR);Fn fn;
    if(path&&n&&n<MAX_PATH&&(id&255)==5){lstrcpyA(path,target);return S_OK;}
    fn=(Fn)GetProcAddress(real_dll(),"SHGetFolderPathA");return fn?fn(w,id,token,flags,path):E_FAIL;
}
