/* Multiplayer saves for Kohan II 1.3.72 (Steam build 25068126).
 *
 * Two switchable features, both made from the game's own save code:
 *  - save copies: when the host saves a multiplayer match, every other player's game writes the same moment of the
 *    match as "<name> (copy).RSG" with the engine's own save task, while it is paused for the host's save anyway;
 *  - multiplayer autosave: the host saves the running match every N minutes into "MP Autosave A" and "B" in turn.
 *
 * Disconnects, the lobby return and drop votes belong to the lobby module's match recovery; this module never acts on
 * them. Saves are coordinated with it through PawSaveBusy (see "shared save contract" below).
 *
 * The launcher's helper loads this module and calls PawInstallQueued while the game's other threads are paused, as
 * for the lobby compatibility module. Every function called and every call or pointer redirected is checked against
 * the 1.3.72 bytes first; a feature whose bytes differ stays off. Autosave runs from the game's main thread (the
 * message pump's PeekMessage, at most every 50 ms). No worker threads or sockets.
 */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include <stdio.h>
#include <stdarg.h>
#ifndef CP_UTF8 /* TCC's Windows headers have no winnls.h; kernel32 exports the function */
#define CP_UTF8 65001
WINBASEAPI int WINAPI WideCharToMultiByte(UINT, DWORD, LPCWSTR, int, LPSTR, int, LPCSTR, LPBOOL);
#endif

#define MAGIC 0x31534d50u /* "PMS1" */
enum { FEATURE_SAVE_COPIES = 1, FEATURE_AUTOSAVE = 2, FEATURE_ALL = 3 };
typedef struct {
    uint32_t magic, features, mainThread, autosaveMinutes;
    wchar_t log[MAX_PATH];
} Config;

/* ------------------------------------------------------------------ 1.3.72 code (RVAs)
 * Patterns: hex bytes, "??" = an absolute operand that relocation changes. Each was re-read from the 1.3.72 image
 * at the address the 1.3.68 module used, found by its 1.3.68 pattern with call offsets ignored. */
typedef struct { const char *name; uint32_t rva; const char *bytes; } Signature;
enum {
    S_GAME_SAVE, S_CLIENT_SAVE_CALL, S_BEGIN_SOLO_TASK, S_CREATE_SAVE_TASK, S_NEW, S_SAVE_TASK_CTOR, S_QUEUE_TASK,
    S_BLOCK_TASK, S_STRING_CTOR, S_STRING_RELEASE, S_USER_DATA, S_WANTED_PACKET, S_SESSION_READ, S_FOR_EACH_CONNECTION,
    S_SCRIPT_READ, S_RUN_SCRIPT, S_SOURCE_WALK, S_SOURCE_STEP, S_SETTINGS_READ, S_SETTINGS_TYPE, S_BACKUP_MODE,
    S_WORLD_READ, S_MANAGER_READ, S_MESSAGE_PUMP, S_SAVE_TASK_SLOT, S_COUNT
};
static const Signature signatures[S_COUNT] = {
    {"KKC_Session::GameSave", 0x162a74, "b8 ?? ?? ?? ?? e8 5e eb 27 00 51 51 53 56 57 8b f9 8b 0d ?? ?? ?? ?? e8"},
    /* push eax; push ebx; call BeginSoloTask; push "NET: GameSave on client" */
    {"GameSave client call", 0x162b92, "50 53 e8 0d 9e fe ff 68 ?? ?? ?? ?? 68 ?? ?? ?? ?? 68 56 0c 00 00"},
    {"TGC_AdminNode::BeginSoloTask", 0x14c9a6, "8a 44 24 04 56 8b f1 88 86 9c 00 00 00 0f b6 c0 50 68"},
    {"KKC_Session::CreateSaveGameTask", 0x16a9f6, "b8 ?? ?? ?? ?? e8 ?? ?? ?? ?? 51 56 6a 2c 8b f1 e8 31 46 18 00 8b d0 59"},
    {"operator new", 0x2ef03c, "8b 44 24 04 83 c0 24 80 3d ?? ?? ?? ?? 00 50 75 07 e8"},
    {"KKC_ResourceManagerTaskSaveGame ctor", 0x1c180a, "55 8b ec 51 8b 45 10 56 57 51 8b 00 8b f9 83 e8 10 89 7d fc"},
    {"resource manager queue", 0x1cc42b, "b8 ?? ?? ?? ?? e8 ?? ?? ?? ?? 51 51 56 57 8b f9 8d 77 20 8b 06 8b ce"},
    {"TGC_ResourceManagerTask::Block", 0x1ce7e9, "51 51 55 8b e9 8b 45 00 ff 50 10 8b cd 84 c0 74 0a e8"},
    {"engine string ctor", 0x205de, "b8 ?? ?? ?? ?? e8 f4 0f 3c 00 51 56 8b f1 6a 02 89 75 f0 e8 33 df 2d 00"},
    {"engine string release", 0x21375, "8b d1 83 c8 ff f0 0f c1 42 0c 48 85 c0 7f 08 8b 0a 52 8b 01 ff 50 04 c3"},
    /* push dword ptr [personal folder]; mov ecx, [esp+8]; push "%USERDATA%"; call; ret 4 */
    {"user data token", 0x1cb7fc, "ff 35 ?? ?? ?? ?? 8b 4c 24 08 68 ?? ?? ?? ?? e8 13 52 e6 ff c2 04 00"},
    /* mov ecx, [admin node] (+5) ... mov eax, [net node] (+0x21) */
    {"TGC_AdminConn::GetWantedPacketType", 0x150009, "56 8b f1 8b 0d ?? ?? ?? ?? 80 b9 ec 02 00 00 00 74 35 3b b1 e4 02 00 00 0f 85 16 01 00 00 6a 02 a1 ?? ?? ?? ?? 8b 8e 2c 04 00 00 f3 0f 10 40 50 33 c0 f3 0f 5c 81 90 00 00 00"},
    /* mov eax, [session] (+1); ...; cmp dword [eax+0xf0], 0 */
    {"session read", 0x15bbe3, "a1 ?? ?? ?? ?? 83 c4 14 85 c0 74 4d 83 b8 f0 00 00 00 00 74 44"},
    {"for each connection", 0x15ba42, "56 8b 71 70 eb 0a 8b 0e e8 ?? ?? ?? ?? 8b 76 04 85 f6 75 f2 5e c3"},
    /* mov ecx, [console] (+2); lea eax, [ebp-0x1c]; add esp, 0x18; push ebx; push eax; call RunScriptCommand */
    {"script command read", 0x13df06, "8b 0d ?? ?? ?? ?? 8d 45 e4 83 c4 18 53 50 e8 4a ba 0b 00"},
    {"run script command", 0x1f9963, "55 8b ec 8b 45 08 53 56 57 8b 00 8b d9 83 e8 10 80 7d 0c 00 74 1a"},
    {"save task source walk", 0x1c1a22, "8b f9 8b 77 1c 8b 46 08 c1 e8 04 a8 01 0f 84 f8 00 00 00 ff 76 38"},
    {"save task source step", 0x1c1a99, "53 e8 f0 a4 fa ff 59 84 c0 74 52 53 e8 0a 3a 00 00 80 3d ?? ?? ?? ?? 00 59 8b 70 50 75 0d 8b 46 08 d1 e8 a8 01"},
    {"save task settings read", 0x1c1a56, "8b 9e 88 00 00 00 83 c4 18"},
    {"settings type check", 0x16bf8f, "8b 4c 24 04 68 ?? ?? ?? ?? 8b 01 ff 50 04 8b c8 e8 ?? ?? ?? ?? c3"},
    /* SetBackupMode: cmp byte [esi+0x2f4], bl (the backup mode flag) */
    {"TGC_AdminNode::SetBackupMode", 0x14f89c, "b8 ?? ?? ?? ?? e8 ?? ?? ?? ?? 83 ec 14 8a 45 08 53 33 db 89 5d f0 56 8b f1 84 c0 0f 84 e4 00 00 00 38 9e f4 02 00 00"},
    /* CreateSaveGameTask: mov eax, [g_gworld] (+1); lea ecx, [esi+0x68] */
    {"save task world read", 0x16aa1c, "a1 ?? ?? ?? ?? 8d 4e 68"},
    /* CreateSaveGameTask: mov ecx, [resource manager] (+2) */
    {"save task manager read", 0x16aa67, "8b 0d ?? ?? ?? ??"},
    /* the main loop's message pump: push PM_REMOVE; push 0 x3; lea eax, [msg]; push eax; call [PeekMessage pointer] */
    {"message pump", 0x2f5388, "6a 01 53 53 53 8d 45 e4 50 ff 15 ?? ?? ?? ?? 85 c0 75 d3"},
    /* CreateSaveGameTask: push eax (the new task); lea ecx, [esi+0x134] (the session's task slot); ...; call assign */
    {"save task slot", 0x16aa36, "50 8d 8e 34 01 00 00 c7 45 fc ff ff ff ff e8 e4 04 fa ff"},
};
/* The one call site redirected (opcode E8) and the function it must call now. */
#define CLIENT_SAVE_CALL 0x162b94
#define PEEK_POINTER 0x5d4314 /* the pointer the message pump calls PeekMessage through (resolved on first use) */
/* Field offsets, each read by the 1.3.72 code in the signature named. */
#define SESSION_FILENAME 0x68    /* engine string; CreateSaveGameTask, GameSave */
#define SESSION_STATE 0xf0       /* 2 game, 4 film, 1 lobby; session read */
#define SESSION_SAVE_TASK 0x134  /* the host's current save task, cleared when it has ended; save task slot */
#define ADMIN_SERVER 0x2e4       /* GetWantedPacketType */
#define ADMIN_BACKUP_MODE 0x2f4  /* byte; SetBackupMode */
#define NETNODE_CONNECTIONS 0x70 /* for each connection: {net connection, next} list */
#define WORLD_RESOURCE 0x28
#define RES_FLAGS 0x08
#define RES_CREATE_SETTINGS 0x88
#define SETTINGS_SOURCE 0x50
#define RES_MADE_FROM_OTHER (1u << 4)
#define RES_HEADER_LOADED (1u << 1)
#define TASK_REFS 0x04
#define TASK_STATUS 0x14 /* 2 finished, 3 failed */

/* thiscall functions are called as fastcall with an unused EDX: same registers, callee pops the stack arguments. */
typedef void *(__attribute__((fastcall)) *StringCtor)(void *self, void *edx, const wchar_t *text);
typedef void (__attribute__((fastcall)) *StringRelease)(void *header, void *edx);
typedef void (__attribute__((fastcall)) *RunScript)(void *console, void *edx, void *command, int flag);
typedef void (__attribute__((fastcall)) *BeginSoloTask)(void *admin, void *edx, int flag, void *node);
typedef void *(__cdecl *EngineNew)(size_t size);
typedef void *(__attribute__((fastcall)) *SaveTaskCtor)(void *self, void *edx, void *world, int isMap, void *name);
typedef void (__attribute__((fastcall)) *QueueTask)(void *manager, void *edx, void *task);
typedef void (__attribute__((fastcall)) *BlockTask)(void *task, void *edx);
typedef void *(__attribute__((fastcall)) *DeletingDtor)(void *self, void *edx, int flags);
/* returns a C++ bool: only AL is set, the rest of EAX is whatever the callee left there */
typedef unsigned char (__cdecl *IsFromResource)(void *settings);
typedef BOOL (WINAPI *PeekFn)(MSG *message, HWND window, UINT first, UINT last, UINT remove);
typedef BOOL (WINAPI *EnumModulesFn)(HANDLE process, HMODULE *modules, DWORD size, DWORD *needed);

static uintptr_t image;
static uint32_t imageSize;
static Config config;
static uint32_t features;
static uintptr_t consoleGlobal, sessionGlobal, adminGlobal, netNodeGlobal, worldGlobal, managerGlobal, personalGlobal;
static PeekFn originalPeek;
static volatile LONG inTick;
static DWORD lastTick;

__declspec(dllexport) volatile LONG PawSavesInstallStatus;  /* 0 not run, 1 installed, -1 failed */
__declspec(dllexport) volatile LONG PawSavesFeatures;       /* features actually on */
__declspec(dllexport) volatile LONG PawSavesCopies, PawSavesCopiesSkipped, PawSavesAutosaves, PawSavesAutosavesDeferred,
    PawSavesTicks;

/* ------------------------------------------------------------------ shared save contract
 * A Paw module that queues or writes a save exports PawSaveBusy and keeps it non-zero from just before it queues the
 * save command until that save has ended (or it gave up waiting). A module queues a save only while no loaded module's
 * PawSaveBusy is non-zero and no native save task is pending in the session. Here it counts this module's saves in
 * flight: autosave (main thread) and save copies (simulation thread). */
__declspec(dllexport) volatile LONG PawSaveBusy;
#define OTHER_MODULES 8
static volatile LONG *otherBusy[OTHER_MODULES]; /* other loaded modules' PawSaveBusy */
static int otherCount;
static DWORD otherScanned; /* main thread: when the module list was last read; 0 = never */

/* ------------------------------------------------------------------ log */
static void log_line(const char *format, ...) {
    char text[1024];
    SYSTEMTIME t;
    va_list args;
    int n;
    HANDLE file;
    DWORD written;
    if (!config.log[0]) return;
    GetLocalTime(&t);
    n = _snprintf(text, sizeof text, "%04u-%02u-%02u %02u:%02u:%02u ", t.wYear, t.wMonth, t.wDay, t.wHour, t.wMinute,
                  t.wSecond);
    if (n < 0) return;
    va_start(args, format);
    int m = _vsnprintf(text + n, sizeof text - n - 2, format, args);
    va_end(args);
    if (m < 0) m = (int)strlen(text + n);
    n += m;
    text[n++] = '\r';
    text[n++] = '\n';
    file = CreateFileW(config.log, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_ALWAYS,
                       FILE_ATTRIBUTE_NORMAL, 0);
    if (file == INVALID_HANDLE_VALUE) return;
    WriteFile(file, text, (DWORD)n, &written, 0);
    CloseHandle(file);
}
static const char *utf8(const wchar_t *text, char *out, int size) {
    if (!text || !WideCharToMultiByte(CP_UTF8, 0, text, -1, out, size, 0, 0)) out[0] = 0;
    return out;
}

/* ------------------------------------------------------------------ code checks */
static int hex(char c) {
    return c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : -1;
}
/* 1 when the bytes at base+rva match the pattern, entirely inside an image of `size` bytes. */
static int matches_at(uintptr_t base, uint32_t size, uint32_t rva, const char *p) {
    const unsigned char *b = (const unsigned char *)(base + rva);
    uint32_t n = 0;
    const char *q;
    for (q = p; *q; q++) if (*q != ' ') n++;
    if (n % 2 || (uint64_t)rva + n / 2 > size) return 0;
    while (*p) {
        if (*p == ' ') { p++; continue; }
        if (p[0] == '?' && p[1] == '?') { p += 2; b++; continue; }
        if (hex(p[0]) < 0 || hex(p[1]) < 0 || *b != (unsigned char)(hex(p[0]) << 4 | hex(p[1]))) return 0;
        p += 2;
        b++;
    }
    return 1;
}
/* Every signature in `list` (-1 terminated) matches; logs the first that does not. */
static int signatures_match(uintptr_t base, uint32_t size, const int *list, const char *feature) {
    for (; *list >= 0; list++)
        if (!matches_at(base, size, signatures[*list].rva, signatures[*list].bytes)) {
            log_line("%s: off, k2.exe code differs at %s (RVA 0x%06X)", feature, signatures[*list].name,
                     signatures[*list].rva);
            return 0;
        }
    return 1;
}
static uintptr_t call_target(uintptr_t site) {
    int32_t rel;
    if (*(const unsigned char *)site != 0xe8) return 0;
    memcpy(&rel, (const void *)(site + 1), 4);
    return site + 5 + rel;
}
static uint32_t operand(uint32_t rva, uint32_t offset) { return *(const uint32_t *)(image + rva + offset); }

/* Points the call at `site` to `stub` if it currently calls `expected`; 0 and no change otherwise. */
static int redirect_call(uintptr_t site, uintptr_t expected, const void *stub) {
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

/* ------------------------------------------------------------------ plain-memory state (tested offline) */
#define FIELD(type, object, offset) (*(type const *)((const char *)(object) + (offset)))

/* Autosave: this PC hosts the running match (its admin node is the server, the session is in state 2), outside
 * backup mode, and someone else is connected. A PC that took over from a lost host counts. */
static int hosting_match(const void *admin, const void *session, const void *netNode) {
    if (!admin || !session || FIELD(const void *, admin, ADMIN_SERVER) != admin) return 0;
    return FIELD(int, session, SESSION_STATE) == 2 && !FIELD(uint8_t, admin, ADMIN_BACKUP_MODE) && netNode &&
           FIELD(const void *, netNode, NETNODE_CONNECTIONS) != 0;
}
/* "Save/X.RSG" -> "Save/X (copy).RSG"; 0 when `name` is not a plain relative path to an .RSG file. */
static int copy_name(const wchar_t *name, wchar_t *out, size_t size) {
    size_t n = name ? wcslen(name) : 0;
    if (n < 5 || n + 8 > size || wcsstr(name, L"..") || wcschr(name, L':') || name[0] == L'/' || name[0] == L'\\' ||
        _wcsicmp(name + n - 4, L".rsg"))
        return 0;
    memcpy(out, name, (n - 4) * sizeof(wchar_t));
    wcscpy(out + n - 4, L" (copy)");
    wcscat(out, name + n - 4);
    return 1;
}
/* The checksum the lobby compares for a saved game: h = h * 7 + byte over the whole file (KKC_Session). */
static uint32_t lobby_checksum(const unsigned char *data, size_t size, uint32_t h) {
    size_t i;
    for (i = 0; i < size; i++) h = h * 7 + data[i];
    return h;
}

/* ------------------------------------------------------------------ game access */
static void *pointer_at(uintptr_t global) { return global ? *(void *const *)global : 0; }
/* The game's work depot: its own "%USERDATA%" folder (Documents\Kohan2, as the game resolved it) + "\data". */
static int user_data(wchar_t *out, size_t size) {
    const wchar_t *text = personalGlobal ? *(const wchar_t *const *)personalGlobal : 0;
    size_t i, n = text ? wcslen(text) : 0;
    if (!n || n + 6 > size) return 0;
    for (i = 0; i <= n; i++) out[i] = text[i] == L'/' ? L'\\' : text[i];
    if (out[n - 1] != L'\\') wcscat(out, L"\\");
    wcscat(out, L"data");
    return 1;
}
static int on_disk(const wchar_t *relative, wchar_t *out, size_t size) {
    size_t i, n;
    if (!user_data(out, size)) return 0;
    n = wcslen(out);
    if (n + 1 + wcslen(relative) + 1 > size) return 0;
    out[n++] = L'\\';
    for (i = 0; relative[i]; i++) out[n++] = relative[i] == L'/' ? L'\\' : relative[i];
    out[n] = 0;
    return 1;
}
/* Queues one script command (the game's console), as a "+command" on the command line does. Main thread only. */
static int run_script_in_game(const wchar_t *command) {
    void *console = pointer_at(consoleGlobal), *text = 0;
    if (!console) return 0;
    ((StringCtor)(image + 0x205de))(&text, 0, command); /* an engine string is one pointer to its text */
    ((RunScript)(image + 0x1f9963))(console, 0, &text, 0);
    ((StringRelease)(image + 0x21375))((char *)text - 0x10, 0);
    return 1;
}
static int (*run_script)(const wchar_t *command) = run_script_in_game; /* the offline checks count commands instead */
static void session_file(const void *session, wchar_t *out, size_t size) {
    const wchar_t *text = session ? FIELD(const wchar_t *, session, SESSION_FILENAME) : 0;
    out[0] = 0;
    if (text && wcslen(text) < size) wcscpy(out, text);
}
static int file_checksum(const wchar_t *path, uint32_t *checksum, uint32_t *bytes) {
    unsigned char buffer[65536];
    DWORD got;
    uint32_t h = 0, total = 0;
    HANDLE file = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_EXISTING, 0, 0);
    if (file == INVALID_HANDLE_VALUE) return 0;
    while (ReadFile(file, buffer, sizeof buffer, &got, 0) && got) { h = lobby_checksum(buffer, got, h); total += got; }
    CloseHandle(file);
    *checksum = h;
    *bytes = total;
    return 1;
}
/* "<what> "<name>": <bytes> bytes, lobby checksum <n>" for a save under the game's data folder (names relative to it:
 * no user name or Documents path in the log). */
static void log_checksum(const char *what, const wchar_t *relative) {
    wchar_t path[MAX_PATH];
    char a[MAX_PATH * 3];
    uint32_t checksum, bytes;
    if (on_disk(relative, path, MAX_PATH) && file_checksum(path, &checksum, &bytes))
        log_line("%s \"%s\": %lu bytes, lobby checksum %ld", what, utf8(relative, a, sizeof a), (unsigned long)bytes,
                 (long)(int32_t)checksum);
    else
        log_line("%s \"%s\": not found", what, utf8(relative, a, sizeof a));
}

/* ------------------------------------------------------------------ shared save contract: other modules */
/* Main thread. Reads the loaded modules' PawSaveBusy exports again at most once a second. The helpers load Paw modules
 * into the game with LoadLibraryW and never free them, so the pointers stay valid. */
static void scan_other_modules(DWORD now) {
    static EnumModulesFn enumerate;
    HMODULE modules[256], self = 0;
    DWORD needed = 0, i, n;
    if (otherScanned && now - otherScanned < 1000) return;
    otherScanned = now ? now : 1;
    if (!enumerate) enumerate = (EnumModulesFn)GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "K32EnumProcessModules");
    if (!enumerate || !enumerate(GetCurrentProcess(), modules, sizeof modules, &needed)) return;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                       (LPCWSTR)(const void *)&PawSaveBusy, &self);
    n = needed / sizeof(HMODULE);
    if (n > 256) n = 256;
    otherCount = 0;
    for (i = 0; i < n && otherCount < OTHER_MODULES; i++) {
        volatile LONG *busy;
        if (modules[i] == self) continue;
        busy = (volatile LONG *)GetProcAddress(modules[i], "PawSaveBusy");
        if (busy) otherBusy[otherCount++] = busy;
    }
}
static int others_busy(void) {
    int i;
    for (i = 0; i < otherCount; i++) if (*otherBusy[i]) return 1;
    return 0;
}

/* ------------------------------------------------------------------ save copies */
static BeginSoloTask originalBeginSoloTask;
static IsFromResource isFromResource;
__declspec(dllexport) volatile LONG PawSavesCopiesUntracked;

static void release_task(void *task) {
    if (!InterlockedDecrement((volatile LONG *)((char *)task + TASK_REFS)))
        ((DeletingDtor)(*(void ***)task)[0])(task, 0, 1);
}
/* The walk the save task starts with (KKC_ResourceManagerTaskSaveGame::SaveGame, RVA 1C1A15), read-only, so a save is
 * only attempted where the game's own code would not stop: 0 ready, 1 a resource without create settings, 2 a source
 * whose header is not loaded, 3 a chain too long, -1 nothing to save. A client whose lobby first picked a saved game
 * and then a random map stopped here on 1.3.68. On 1.3.72 a player who received a saved match over the network has no
 * header loaded for that save; saving there asserts "source_res->IsHeaderLoaded()" on the ASYNC thread while it holds
 * a lock the main thread waits for, and the game ends with "timeout waiting for mutex". Copies and autosaves check
 * first. (The game's cast helper 5C54B4 returns its argument whenever the type check passed, so +0x50 is read
 * directly.) */
static const char *const sourceProblem[] = {"there is no world", "", "has no create settings", "is not loaded",
    "is too deep"}; /* by sources_ready() + 1 */
static int sources_ready(int *depthOut) {
    const void *world = pointer_at(worldGlobal), *res, *settings;
    int depth;
    *depthOut = 0;
    if (!world || !(res = FIELD(const void *, world, WORLD_RESOURCE))) return -1;
    if (!(FIELD(uint32_t, res, RES_FLAGS) & RES_MADE_FROM_OTHER)) return 0;
    for (depth = 0; depth < 16; depth++) {
        *depthOut = depth + 1;
        settings = FIELD(void *, res, RES_CREATE_SETTINGS);
        if (!settings) return 1;
        if (!isFromResource((void *)settings)) return 0;
        res = FIELD(const void *, settings, SETTINGS_SOURCE);
        if (!res) return 1;
        if (!(FIELD(uint32_t, res, RES_FLAGS) & RES_HEADER_LOADED)) return 2;
    }
    return 3;
}
/* What CreateSaveGameTask does (new task, construct, queue) with a task of ours, then the engine's own wait for it
 * (TGC_ResourceManagerTask::Block runs a task that has not started right here). The game is paused for the host's
 * save at this point. Returns the task's final status (2 finished) or a negative code. */
static int write_copy(const wchar_t *name) {
    void *world = pointer_at(worldGlobal), *manager = pointer_at(managerGlobal), *resource, *task, *text = 0;
    int status;
    if (!world || !manager || !(resource = FIELD(void *, world, WORLD_RESOURCE))) return -2;
    ((StringCtor)(image + 0x205de))(&text, 0, name);
    task = ((EngineNew)(image + 0x2ef03c))(0x2c);
    if (task) ((SaveTaskCtor)(image + 0x1c180a))(task, 0, resource, 0, &text);
    ((StringRelease)(image + 0x21375))((char *)text - 0x10, 0);
    if (!task) return -3;
    InterlockedIncrement((volatile LONG *)((char *)task + TASK_REFS)); /* ours, released below */
    ((QueueTask)(image + 0x1cc42b))(manager, 0, task);
    ((BlockTask)(image + 0x1ce7e9))(task, 0);
    status = FIELD(int, task, TASK_STATUS);
    release_task(task);
    return status;
}
static void on_client_save(void) {
    wchar_t name[MAX_PATH], copy[MAX_PATH];
    char a[MAX_PATH * 3], b[MAX_PATH * 3];
    const void *session = pointer_at(sessionGlobal);
    int state = session ? FIELD(int, session, SESSION_STATE) : -1, ready, status, depth;
    DWORD start;
    session_file(session, name, MAX_PATH);
    if ((state != 2 && state != 4) || !copy_name(name, copy, MAX_PATH)) {
        InterlockedIncrement(&PawSavesCopiesSkipped);
        log_line("save copy skipped: file \"%s\", session state %d", utf8(name, a, sizeof a), state);
        return;
    }
    ready = sources_ready(&depth);
    if (ready) {
        InterlockedIncrement(&PawSavesCopiesSkipped);
        log_line("save copy of \"%s\" skipped: the game's save code would stop at an assertion here (map source %d %s)",
                 utf8(name, a, sizeof a), depth, sourceProblem[ready + 1]);
        return;
    }
    start = GetTickCount();
    InterlockedIncrement(&PawSaveBusy);
    status = write_copy(copy);
    InterlockedDecrement(&PawSaveBusy);
    if (status != 2) {
        InterlockedIncrement(&PawSavesCopiesSkipped);
        log_line("save copy of \"%s\" did not finish (status %d)", utf8(name, a, sizeof a), status);
        return;
    }
    InterlockedIncrement(&PawSavesCopies);
    log_line("save copy \"%s\" written in %lu ms", utf8(copy, b, sizeof b), (unsigned long)(GetTickCount() - start));
    log_checksum("save copy", copy);
}
/* The client branch's call of BeginSoloTask (thiscall, 2 arguments): unchanged, then the copy, on the thread that
 * applies the save order (SIM in 1.3.72). */
static void __attribute__((fastcall)) client_save_hook(void *admin, void *edx, int flag, void *node) {
    originalBeginSoloTask(admin, edx, flag, node);
    if (features & FEATURE_SAVE_COPIES) on_client_save();
}
static const int saveCopyNeeds[] = {S_GAME_SAVE, S_CLIENT_SAVE_CALL, S_BEGIN_SOLO_TASK, S_NEW, S_SAVE_TASK_CTOR,
    S_QUEUE_TASK, S_BLOCK_TASK, S_STRING_CTOR, S_STRING_RELEASE, S_SOURCE_WALK, S_SOURCE_STEP, S_SETTINGS_READ,
    S_SETTINGS_TYPE, S_WORLD_READ, S_MANAGER_READ, S_SESSION_READ, S_USER_DATA, -1};

/* ------------------------------------------------------------------ autosave */
/* Main thread. Due every N minutes while this PC hosts a match. A save is queued only when nothing save-related is
 * busy: no native save task in the session, no own save in flight, no other module's PawSaveBusy, and the game's save
 * code can save this match here. Busy time defers the save; it does not skip it. An own save is in flight from the
 * GameSave command until its task has appeared in the session's task slot and left it again, at most 30 s for the task
 * to appear and 2 minutes in all. */
enum { AUTOSAVE_IDLE, AUTOSAVE_QUEUED, AUTOSAVE_RUNNING };
static DWORD autosaveSince; /* when the hosted match was first seen, then the last save; 0 = none */
static int autosaveSlot, autosaveBlocked, autosaveDeferred, autosaveState;
static DWORD autosaveQueuedAt;
static const void *autosaveTask;
static wchar_t autosaveName[32];
static void autosave_end(const char *how) {
    char a[64];
    if (how) log_line("autosave \"%s\" %s", utf8(autosaveName, a, sizeof a), how);
    else log_checksum("autosave", autosaveName);
    autosaveState = AUTOSAVE_IDLE;
    autosaveTask = 0;
    InterlockedDecrement(&PawSaveBusy);
}
static void autosave_follow(DWORD now) {
    const void *session = pointer_at(sessionGlobal);
    const void *task = session ? FIELD(const void *, session, SESSION_SAVE_TASK) : 0;
    if (autosaveState == AUTOSAVE_QUEUED) {
        if (task) {
            autosaveState = AUTOSAVE_RUNNING;
            autosaveTask = task;
        } else if (now - autosaveQueuedAt >= 30000) autosave_end("did not start in 30 s");
        return;
    }
    if (task != autosaveTask) autosave_end(0); /* the session released it */
    else if (now - autosaveQueuedAt >= 120000) autosave_end("did not end in 2 minutes");
}
static void autosave_tick(DWORD now) {
    static const wchar_t *const names[] = {L"Save/MP Autosave A.RSG", L"Save/MP Autosave B.RSG"};
    const void *session = pointer_at(sessionGlobal);
    wchar_t command[64];
    int ready, depth;
    if (autosaveState != AUTOSAVE_IDLE) autosave_follow(now);
    if (!hosting_match(pointer_at(adminGlobal), session, pointer_at(netNodeGlobal))) {
        if (autosaveSince) log_line("autosave: the match this PC hosted is over");
        autosaveSince = 0;
        autosaveBlocked = autosaveDeferred = 0;
        return;
    }
    if (!autosaveSince) {
        autosaveSince = now ? now : 1;
        log_line("autosave: hosting a match; first save in %lu minute(s)", (unsigned long)config.autosaveMinutes);
        return;
    }
    if (now - autosaveSince < config.autosaveMinutes * 60000u) return;
    scan_other_modules(now);
    if (autosaveState != AUTOSAVE_IDLE || FIELD(const void *, session, SESSION_SAVE_TASK) || others_busy()) {
        if (!autosaveDeferred) {
            InterlockedIncrement(&PawSavesAutosavesDeferred);
            log_line("autosave: deferred, another save is %s", autosaveState != AUTOSAVE_IDLE ? "this module's" :
                     FIELD(const void *, session, SESSION_SAVE_TASK) ? "running in the game" : "queued by another module");
        }
        autosaveDeferred = 1;
        return;
    }
    autosaveDeferred = 0;
    if ((ready = sources_ready(&depth)) != 0) {
        autosaveSince = now ? now : 1;
        if (!autosaveBlocked)
            log_line("autosave: not saving this match: the game's save code would stop at an assertion on this PC "
                     "(map source %d %s)", depth, sourceProblem[ready + 1]);
        autosaveBlocked = 1;
        return;
    }
    wcscpy(autosaveName, names[autosaveSlot]);
    _snwprintf(command, 64, L"GameSave \"%ls\"", autosaveName);
    command[63] = 0;
    InterlockedIncrement(&PawSaveBusy); /* before the command: other modules see it from now on */
    if (!run_script(command)) {
        InterlockedDecrement(&PawSaveBusy);
        return;
    }
    autosaveState = AUTOSAVE_QUEUED;
    autosaveQueuedAt = now;
    autosaveTask = 0;
    InterlockedIncrement(&PawSavesAutosaves);
    log_line("autosave: saving \"MP Autosave %c\"", autosaveSlot ? 'B' : 'A');
    autosaveSince = now ? now : 1;
    autosaveSlot ^= 1;
}
static const int autosaveNeeds[] = {S_SESSION_READ, S_FOR_EACH_CONNECTION, S_WANTED_PACKET, S_GAME_SAVE,
    S_SCRIPT_READ, S_RUN_SCRIPT, S_STRING_CTOR, S_STRING_RELEASE, S_BACKUP_MODE, S_SOURCE_WALK, S_SOURCE_STEP,
    S_SETTINGS_READ, S_SETTINGS_TYPE, S_WORLD_READ, S_CREATE_SAVE_TASK, S_SAVE_TASK_SLOT, S_USER_DATA, -1};

/* ------------------------------------------------------------------ main-thread tick */
static void tick(void) {
    DWORD now = GetTickCount();
    InterlockedIncrement(&PawSavesTicks);
    if (features & FEATURE_AUTOSAVE) autosave_tick(now);
}
/* The message pump's PeekMessage (every frame, on the game's main thread): unchanged, plus the tick at most every
 * 50 ms, between messages, where the game also handles a player's clicks. */
static BOOL WINAPI peek_hook(MSG *message, HWND window, UINT first, UINT last, UINT remove) {
    BOOL got = originalPeek(message, window, first, last, remove);
    DWORD now = GetTickCount();
    if (GetCurrentThreadId() == config.mainThread && now - lastTick >= 50 && !InterlockedExchange(&inTick, 1)) {
        lastTick = now;
        tick();
        InterlockedExchange(&inTick, 0);
    }
    return got;
}

/* ------------------------------------------------------------------ install */
/* The pump's pointer, once the game resolved it on first use: user32's PeekMessageW (or A). Before that it points at
 * the resolver, which would overwrite a replacement. */
static int peek_entry(PeekFn *current) {
    HMODULE user32 = GetModuleHandleW(L"user32.dll");
    PeekFn value = *(PeekFn *)(image + PEEK_POINTER);
    if (operand(signatures[S_MESSAGE_PUMP].rva, 11) != image + PEEK_POINTER || !user32 ||
        (value != (PeekFn)GetProcAddress(user32, "PeekMessageW") && value != (PeekFn)GetProcAddress(user32, "PeekMessageA")))
        return 0;
    *current = value;
    return 1;
}
static int write_peek(PeekFn value) {
    DWORD old, ignored;
    void *slot = (void *)(image + PEEK_POINTER);
    if (!VirtualProtect(slot, 4, PAGE_READWRITE, &old)) return 0;
    *(PeekFn *)slot = value;
    VirtualProtect(slot, 4, old, &ignored);
    return 1;
}
/* Called by the fresh-launch installer while the game's other threads are paused. */
__declspec(dllexport) DWORD WINAPI PawInstall(void *raw) {
    Config *cfg = raw;
    uintptr_t peb;
    uint32_t on = 0;
    if (PawSavesInstallStatus) return 10;
    if (!cfg || cfg->magic != MAGIC || !cfg->mainThread || (cfg->features & ~FEATURE_ALL) || cfg->autosaveMinutes < 1 ||
        cfg->autosaveMinutes > 120)
        return 11;
    memcpy(&config, cfg, sizeof config);
    config.log[MAX_PATH - 1] = 0;
    /* The main image from the x86 PEB, without the loader lock (other threads are paused). */
    __asm__("movl %%fs:0x30, %0" : "=r"(peb));
    image = *(uintptr_t *)(peb + 8);
    if (*(uint16_t *)image != 0x5a4d) return 13;
    imageSize = *(uint32_t *)(image + *(uint32_t *)(image + 0x3c) + 0x50);
    if (!signatures_match(image, imageSize, (const int[]){S_SCRIPT_READ, S_RUN_SCRIPT, S_STRING_CTOR,
            S_STRING_RELEASE, S_SESSION_READ, S_WANTED_PACKET, S_USER_DATA, S_BACKUP_MODE, S_MESSAGE_PUMP, -1},
            "multiplayer saves"))
        return 21;
    if (!peek_entry(&originalPeek)) { log_line("off: the message pump's PeekMessage is not resolved to user32"); return 20; }
    consoleGlobal = operand(0x13df06, 2);
    sessionGlobal = operand(0x15bbe3, 1);
    adminGlobal = operand(0x150009, 5);
    netNodeGlobal = operand(0x150009, 0x21);
    personalGlobal = operand(0x1cb7fc, 2);
    worldGlobal = operand(0x16aa1c, 1);
    managerGlobal = operand(0x16aa67, 2);
    isFromResource = (IsFromResource)(image + 0x16bf8f);
    if ((cfg->features & FEATURE_SAVE_COPIES) && signatures_match(image, imageSize, saveCopyNeeds, "save copies")) {
        originalBeginSoloTask = (BeginSoloTask)(image + 0x14c9a6);
        if (redirect_call(image + CLIENT_SAVE_CALL, image + 0x14c9a6, client_save_hook)) on |= FEATURE_SAVE_COPIES;
        else log_line("save copies: off, the client save call does not call BeginSoloTask (changed by another module)");
    }
    if ((cfg->features & FEATURE_AUTOSAVE) && signatures_match(image, imageSize, autosaveNeeds, "autosave"))
        on |= FEATURE_AUTOSAVE;
    features = on;
    if (!on || !write_peek(peek_hook)) {
        /* nothing to tick: put the redirected call back */
        if (on & FEATURE_SAVE_COPIES)
            redirect_call(image + CLIENT_SAVE_CALL, (uintptr_t)client_save_hook, (void *)(image + 0x14c9a6));
        features = 0;
        PawSavesInstallStatus = -1;
        log_line("off: no feature could be installed");
        return 40;
    }
    PawSavesFeatures = (LONG)on;
    PawSavesInstallStatus = 1;
    log_line("installed for k2.exe 1.3.72: save copies %s, autosave %s (%lu min)", on & FEATURE_SAVE_COPIES ? "on" : "off",
             on & FEATURE_AUTOSAVE ? "on" : "off", (unsigned long)config.autosaveMinutes);
    return 0;
}
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID unused) { return TRUE; }
/* Finish DLL_THREAD_ATTACH before the launcher pauses other threads (as the lobby module). */
typedef struct { volatile LONG ready; volatile LONG go; DWORD result; Config config; } InstallRequest;
__declspec(dllexport) DWORD WINAPI PawInstallQueued(void *raw) {
    InstallRequest *request = raw;
    DWORD result;
    if (!request) return 50;
    InterlockedExchange(&request->ready, 1);
    while (!request->go) Sleep(1);
    result = PawInstall(&request->config);
    request->result = result;
    InterlockedExchange(&request->ready, 2);
    return result;
}

#ifdef PAW_TEST
#include <stdlib.h>
/* Offline checks of everything that is plain memory or plain logic; no game. With an image file argument (a mapped
 * 1.3.72 k2.exe), also every signature and call site. */
static unsigned checks;
static int failed;
static void check(int ok, const char *what) {
    checks++;
    if (!ok) { failed = 1; printf("FAIL %s\n", what); }
}
static unsigned char admin[0x400], session[0x200], netNode[0x80], node1[8], world[0x40], typeObject[0x10], task1[0x2c],
    task2[0x2c];
static void *adminPointer, *sessionPointer, *netNodePointer, *worldPointer;
/* a map resource chain for sources_ready(): world -> made -> settings -> source */
static unsigned char made[0x90], settings[0x60], source[0x90], settings2[0x60];
static unsigned char __cdecl fake_is_from_resource(void *object) { return object == settings || object == settings2; }
static int commands;
static wchar_t lastCommand[64];
static int count_command(const wchar_t *command) { commands++; wcsncpy(lastCommand, command, 63); return 1; }
static void chain(void) {
    memset(made, 0, sizeof made); memset(settings, 0, sizeof settings); memset(source, 0, sizeof source);
    memset(settings2, 0, sizeof settings2); memset(world, 0, sizeof world);
    worldPointer = world; worldGlobal = (uintptr_t)&worldPointer; isFromResource = fake_is_from_resource;
    *(void **)(world + WORLD_RESOURCE) = made;
    *(uint32_t *)(made + RES_FLAGS) = RES_MADE_FROM_OTHER;
    *(void **)(made + RES_CREATE_SETTINGS) = settings;
    *(void **)(settings + SETTINGS_SOURCE) = source;
    *(uint32_t *)(source + RES_FLAGS) = RES_HEADER_LOADED;
    *(void **)(source + RES_CREATE_SETTINGS) = typeObject; /* settings of another kind (a random map): the walk ends */
}
/* A hosted running match with one other player, saves possible, nothing busy, autosave due at 61 s. */
static void hosting(void) {
    memset(admin, 0, sizeof admin); memset(session, 0, sizeof session); memset(netNode, 0, sizeof netNode);
    *(void **)(admin + ADMIN_SERVER) = admin;
    *(int *)(session + SESSION_STATE) = 2;
    *(void **)(netNode + NETNODE_CONNECTIONS) = node1;
    adminPointer = admin; sessionPointer = session; netNodePointer = netNode;
    adminGlobal = (uintptr_t)&adminPointer; sessionGlobal = (uintptr_t)&sessionPointer; netNodeGlobal = (uintptr_t)&netNodePointer;
    chain();
    run_script = count_command; commands = 0;
    config.autosaveMinutes = 1;
    autosaveSince = 1000; autosaveState = AUTOSAVE_IDLE; autosaveBlocked = autosaveDeferred = 0; autosaveSlot = 0;
    PawSaveBusy = 0; otherCount = 0; otherScanned = 62000; /* no module scan in the offline checks */
}
/* thiscall with two stack arguments; 1 when ESP, EBX, ESI, EDI and EBP are the same afterwards. The values go to static
 * memory: EAX, ECX and EDX belong to the callee. */
uint32_t pawRegsBefore[5], pawRegsAfter[5];
#define SAVE_REGS(to) "movl %%esp, " to "\n\tmovl %%ebx, " to "+4\n\tmovl %%esi, " to "+8\n\tmovl %%edi, " to "+12\n\tmovl %%ebp, " to "+16\n\t"
static int thiscall2(void *fn, void *self, uint32_t first, uint32_t second) {
    __asm__ volatile(SAVE_REGS("pawRegsBefore") "pushl %2\n\tpushl %1\n\tmovl %0, %%ecx\n\tcall *%3\n\t" SAVE_REGS("pawRegsAfter")
                     : : "m"(self), "m"(first), "m"(second), "m"(fn) : "eax", "ecx", "edx", "memory");
    return !memcmp(pawRegsBefore, pawRegsAfter, sizeof pawRegsBefore);
}
static void *seenSelf, *seenFirst;
static int seenSecond;
static void __attribute__((fastcall)) fake_flag_node(void *self, void *edx, int flag, void *node) {
    seenSelf = self; seenSecond = flag; seenFirst = node;
}
int main(int argc, char **argv) {
    wchar_t out[MAX_PATH];
    unsigned i;
    int depth;
    volatile LONG otherFlag = 0;
    /* autosave decisions */
    hosting(); check(hosting_match(admin, session, netNode), "hosting with players");
    admin[ADMIN_BACKUP_MODE] = 1; check(!hosting_match(admin, session, netNode), "no autosave in backup mode");
    hosting(); *(void **)(netNode + NETNODE_CONNECTIONS) = 0; check(!hosting_match(admin, session, netNode), "alone");
    hosting(); *(void **)(admin + ADMIN_SERVER) = session; check(!hosting_match(admin, session, netNode), "client");
    hosting(); *(int *)(session + SESSION_STATE) = 1; check(!hosting_match(admin, session, netNode), "lobby is not a match");
    hosting(); autosave_tick(62000);
    check(commands == 1 && !wcscmp(lastCommand, L"GameSave \"Save/MP Autosave A.RSG\"") && PawSaveBusy == 1 &&
          autosaveState == AUTOSAVE_QUEUED, "idle: one autosave queued, busy from the command on");
    autosave_tick(62050); check(commands == 1, "queued: no second command");
    *(void **)(session + SESSION_SAVE_TASK) = task1; autosave_tick(62100);
    check(autosaveState == AUTOSAVE_RUNNING && PawSaveBusy == 1, "the task appeared: still busy");
    *(void **)(session + SESSION_SAVE_TASK) = 0; autosave_tick(70000);
    check(autosaveState == AUTOSAVE_IDLE && PawSaveBusy == 0 && commands == 1, "the session released it: idle");
    autosave_tick(122100); check(commands == 2 && !wcscmp(lastCommand, L"GameSave \"Save/MP Autosave B.RSG\""), "next interval: slot B");
    /* the reviewer's three cases, each due at 62 s */
    hosting(); *(void **)(session + SESSION_SAVE_TASK) = task2; autosave_tick(62000);
    check(commands == 0 && autosaveDeferred && PawSaveBusy == 0, "native save pending: deferred, nothing queued");
    *(void **)(session + SESSION_SAVE_TASK) = 0; autosave_tick(62050);
    check(commands == 1, "native save ended: the deferred autosave runs");
    hosting(); otherBusy[0] = &otherFlag; otherCount = 1; otherFlag = 1; autosave_tick(62000);
    check(commands == 0 && autosaveDeferred, "another module busy (its save queued, task not yet visible): deferred");
    otherFlag = 0; autosave_tick(62050); check(commands == 1, "the other module idle again: the autosave runs");
    hosting(); autosave_tick(62000); autosaveSince = 1000; autosave_tick(70000);
    check(commands == 1 && autosaveState == AUTOSAVE_QUEUED && autosaveDeferred,
          "own save queued and due again before its task is visible: deferred, no second save");
    autosaveSince = 91000; autosave_tick(92000);
    check(autosaveState == AUTOSAVE_IDLE && PawSaveBusy == 0 && commands == 1, "a save that never starts: given up after 30 s");
    hosting(); *(uint32_t *)(source + RES_FLAGS) = 0; autosave_tick(62000); autosave_tick(122100);
    check(commands == 0 && autosaveBlocked && PawSaveBusy == 0, "a match this PC cannot save: no save, said once");
    hosting(); *(int *)(session + SESSION_STATE) = 1; autosaveSince = 5; autosave_tick(62000);
    check(!autosaveSince && commands == 0, "match over: autosave resets");
    /* the source walk */
    chain(); check(sources_ready(&depth) == 0 && depth == 2, "a loaded source: the game's save code can save");
    *(uint32_t *)(source + RES_FLAGS) = 0;
    check(sources_ready(&depth) == 2 && depth == 1, "a source without its header: no save");
    chain(); *(uint32_t *)(made + RES_FLAGS) = 0; check(sources_ready(&depth) == 0 && depth == 0, "not made from another resource");
    chain(); *(void **)(made + RES_CREATE_SETTINGS) = 0; check(sources_ready(&depth) == 1, "no create settings");
    chain(); *(void **)(made + RES_CREATE_SETTINGS) = typeObject; check(sources_ready(&depth) == 0, "settings of another kind end the walk");
    chain(); *(void **)(settings + SETTINGS_SOURCE) = 0; check(sources_ready(&depth) == 1, "settings without a source");
    chain(); *(void **)(source + RES_CREATE_SETTINGS) = settings2; *(void **)(settings2 + SETTINGS_SOURCE) = made;
    *(uint32_t *)(made + RES_FLAGS) |= RES_HEADER_LOADED; check(sources_ready(&depth) == 3, "a looped chain ends");
    chain(); *(void **)(world + WORLD_RESOURCE) = 0; check(sources_ready(&depth) == -1, "no world resource");
    worldPointer = 0; check(sources_ready(&depth) == -1, "no world");
    /* names and checksums */
    check(copy_name(L"Save/X.RSG", out, MAX_PATH) && !wcscmp(out, L"Save/X (copy).RSG"), "copy name");
    check(copy_name(L"Save/MP Autosave A.rsg", out, MAX_PATH) && !wcscmp(out, L"Save/MP Autosave A (copy).rsg"), "copy keeps case");
    {
        static const wchar_t *const bad[] = {L"", L".RSG", L"Save/../x.RSG", L"C:/x.RSG", L"/x.RSG", L"\\x.RSG", L"Save/x.RFM"};
        for (i = 0; i < sizeof bad / sizeof bad[0]; i++) check(!copy_name(bad[i], out, MAX_PATH), "unsafe copy name");
        check(!copy_name(L"Save/x.RSG", out, 12), "copy name longer than the buffer");
    }
    {
        const unsigned char a[] = {1, 2, 3}, b[] = {0xff, 0xff, 0xff, 0xff};
        check(lobby_checksum(a, 3, 0) == ((1u * 7 + 2) * 7 + 3), "lobby checksum");
        check(lobby_checksum(a + 1, 2, lobby_checksum(a, 1, 0)) == lobby_checksum(a, 3, 0), "checksum in pieces");
        check(lobby_checksum(b, 4, 0) == (((255u * 7 + 255) * 7 + 255) * 7 + 255), "checksum bytes are unsigned");
    }
    /* the hook under the game's calling convention: arguments reach the original, the stack and saved registers are
     * balanced */
    originalBeginSoloTask = (BeginSoloTask)fake_flag_node; features = 0;
    check(thiscall2((void *)client_save_hook, admin, 1, (uint32_t)(uintptr_t)world), "client save hook keeps the stack");
    check(seenSelf == admin && seenSecond == 1 && seenFirst == world, "client save hook passes the call on");
    for (i = 0; i < S_COUNT; i++) {
        const char *p = signatures[i].bytes;
        unsigned n = 0, ok = 1;
        for (; *p; p++) if (*p != ' ') { n++; ok &= (hex(*p) >= 0 || *p == '?'); }
        check(ok && n % 2 == 0 && n >= 8 && signatures[i].rva < 0x44c000, "signature table entry");
    }
    if (argc == 2) { /* a mapped image: headers and sections at their RVAs (as the snapshot the helper build uses) */
        FILE *f = fopen(argv[1], "rb");
        static unsigned char *mapped;
        long size;
        if (!f) { printf("cannot read %s\n", argv[1]); return 2; }
        fseek(f, 0, SEEK_END); size = ftell(f); fseek(f, 0, SEEK_SET);
        mapped = malloc(size);
        if (!mapped || fread(mapped, 1, size, f) != (size_t)size) return 2;
        fclose(f);
        for (i = 0; i < S_COUNT; i++) check(matches_at((uintptr_t)mapped, (uint32_t)size, signatures[i].rva, signatures[i].bytes), signatures[i].name);
        check(call_target((uintptr_t)mapped + CLIENT_SAVE_CALL) == (uintptr_t)mapped + 0x14c9a6, "call site target");
    }
    printf("%s %u multiplayer saves checks%s\n", failed ? "FAIL" : "PASS", checks, argc == 2 ? " (with the 1.3.72 image)" : "");
    return failed;
}
#endif
