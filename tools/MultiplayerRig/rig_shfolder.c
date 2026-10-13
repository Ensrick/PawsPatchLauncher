/* Test rig only (tools/MultiplayerRig): a stand-in shfolder.dll that lets two copies of Kohan II 1.3.72 play a LAN
 * match against each other on one PC. Never ships to players; Run-Rig.ps1 copies it into the game folder for a run
 * and removes it afterwards.
 *
 *  - Own Documents folder per copy: k2.exe finds its user folder with LoadLibrary("shfolder.dll") +
 *    SHGetFolderPathW(CSIDL_PERSONAL); shfolder.dll is not a KnownDLL, so this copy in the game folder wins. With
 *    K2_RIG_DOCUMENTS=<folder> it answers CSIDL_PERSONAL with that folder; every other call goes to the system DLL.
 *  - Joining by address: no script command fills the Direct IP dialog's editboxes, so with K2_RIG_JOIN=<ip>:<port>
 *    the dialog's two editbox reads (calls of the widget text getter) answer with that address instead. Installed on
 *    the first SHGetFolderPathW call (game code, after the loader lock), only when the bytes are 1.3.72's; the DLL
 *    then pins itself, because the game unloads shfolder.dll after the call.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>

#define CSIDL_PERSONAL_ID 0x0005
#define CSIDL_FLAG_CREATE_BIT 0x8000
#define WIDGET_TEXT 0x29fc02    /* lea eax, [ecx+0x6c]; ret: the address of a widget's engine string */
#define JOIN_IP_READ 0xef184    /* KKC_JoinDirectDialog join handler: IP editbox text */
#define JOIN_PORT_READ 0xef1a6  /* and port editbox text */
#define STRING_CTOR 0x205de     /* engine string from const wchar_t* (thiscall) */

typedef HRESULT (WINAPI *FolderPathW)(HWND, int, HANDLE, DWORD, LPWSTR);
typedef HRESULT (WINAPI *FolderPathA)(HWND, int, HANDLE, DWORD, LPSTR);
typedef void *(__attribute__((fastcall)) *StringCtor)(void *self, void *edx, const wchar_t *text);

static HMODULE real;
static volatile LONG joinTried;
static uintptr_t image;
static wchar_t joinIp[64], joinPort[16];
static void *ipString, *portString; /* engine strings (one pointer to the text), built on first use, never freed */

static HMODULE system_shfolder(void) {
    wchar_t path[MAX_PATH];
    UINT n;
    if (real) return real;
    n = GetSystemDirectoryW(path, MAX_PATH);
    if (!n || n > MAX_PATH - 16) return 0;
    wcscat(path, L"\\shfolder.dll");
    real = LoadLibraryW(path);
    return real;
}

/* Stand-ins for `call widget text getter` (thiscall, no arguments): same registers, nothing to pop. */
static void *__attribute__((fastcall)) ip_text(void *widget, void *edx) {
    if (!ipString) ((StringCtor)(image + STRING_CTOR))(&ipString, 0, joinIp);
    return &ipString;
}
static void *__attribute__((fastcall)) port_text(void *widget, void *edx) {
    if (!portString) ((StringCtor)(image + STRING_CTOR))(&portString, 0, joinPort);
    return &portString;
}
static uintptr_t call_target(uintptr_t site) {
    int32_t rel;
    if (*(const unsigned char *)site != 0xe8) return 0;
    memcpy(&rel, (const void *)(site + 1), 4);
    return site + 5 + rel;
}
static int redirect(uintptr_t site, uintptr_t expected, const void *stub) {
    DWORD old, ignored;
    int32_t rel;
    if (call_target(site) != expected) return 0;
    rel = (int32_t)((uintptr_t)stub - (site + 5));
    if (!VirtualProtect((void *)site, 5, PAGE_EXECUTE_READWRITE, &old)) return 0;
    memcpy((void *)(site + 1), &rel, 4);
    VirtualProtect((void *)site, 5, old, &ignored);
    FlushInstructionCache(GetCurrentProcess(), (void *)site, 5);
    return 1;
}
static void install_join(void) {
    wchar_t value[80], *colon;
    static const unsigned char getter[] = {0x8d, 0x41, 0x6c, 0xc3};
    HMODULE self;
    if (InterlockedExchange(&joinTried, 1)) return;
    /* The game frees shfolder.dll after asking for the folder and loads it again later: keep this copy loaded, so
     * every later call (and the join stand-ins) still reaches it. GET_MODULE_HANDLE_EX_FLAG_PIN | _FROM_ADDRESS */
    GetModuleHandleExW(0x1 | 0x4, (LPCWSTR)(void *)install_join, &self);
    if (!GetEnvironmentVariableW(L"K2_RIG_JOIN", value, 80) || !(colon = wcsrchr(value, L':')) || colon == value ||
        !colon[1] || colon - value >= 64 || wcslen(colon + 1) >= 16)
        return;
    *colon = 0;
    wcscpy(joinIp, value);
    wcscpy(joinPort, colon + 1);
    image = (uintptr_t)GetModuleHandleW(0);
    if (memcmp((const void *)(image + WIDGET_TEXT), getter, sizeof getter)) return;
    if (redirect(image + JOIN_IP_READ, image + WIDGET_TEXT, ip_text) &&
        !redirect(image + JOIN_PORT_READ, image + WIDGET_TEXT, port_text))
        redirect(image + JOIN_IP_READ, (uintptr_t)ip_text, (void *)(image + WIDGET_TEXT));
}

static HRESULT WINAPI folder_path_w(HWND window, int csidl, HANDLE token, DWORD flags, LPWSTR path) {
    wchar_t over[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"K2_RIG_DOCUMENTS", over, MAX_PATH);
    FolderPathW system;
    install_join();
    if (path && n > 0 && n < MAX_PATH && (csidl & 0xff) == CSIDL_PERSONAL_ID) {
        if (csidl & CSIDL_FLAG_CREATE_BIT) CreateDirectoryW(over, 0);
        wcscpy(path, over);
        return S_OK;
    }
    system = system_shfolder() ? (FolderPathW)GetProcAddress(real, "SHGetFolderPathW") : 0;
    return system ? system(window, csidl, token, flags, path) : E_FAIL;
}
static HRESULT WINAPI folder_path_a(HWND window, int csidl, HANDLE token, DWORD flags, LPSTR path) {
    FolderPathA system = system_shfolder() ? (FolderPathA)GetProcAddress(real, "SHGetFolderPathA") : 0;
    return system ? system(window, csidl, token, flags, path) : E_FAIL;
}
/* The game asks for the plain names. TCC decorates stdcall exports ("_SHGetFolderPathW@20"), so the exports are
 * undecorated assembler entry points that jump to the stdcall functions (stack and registers untouched). */
void *rig_folder_path_w = (void *)folder_path_w, *rig_folder_path_a = (void *)folder_path_a;
__declspec(dllexport) void SHGetFolderPathW(void);
__declspec(dllexport) void SHGetFolderPathA(void);
__asm__(".globl SHGetFolderPathW\nSHGetFolderPathW:\n jmp *rig_folder_path_w\n"
        ".globl SHGetFolderPathA\nSHGetFolderPathA:\n jmp *rig_folder_path_a\n");
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID unused) { return TRUE; }
