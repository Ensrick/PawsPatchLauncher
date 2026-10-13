/* Multiplayer recovery for Kohan II 1.3.72 (Steam build 25068126).
 *
 * Three switchable features, all driven by the game's own commands and orders:
 *  - save copies: when the host saves a multiplayer match, every other player's game writes the same moment of the
 *    match as "<name> (copy).RSG" with the engine's own save task, while it is paused for the host's save anyway;
 *  - multiplayer autosave: the host saves the running match every N minutes into "MP Autosave A" and "B" in turn;
 *  - drop recovery: when a player leaves a running match without asking to (crash, lost connection), the host saves
 *    "MP Resume <date> <time>", pauses, takes everyone back to the lobby still connected ("End Mission") and selects
 *    that save there, so the player can join again and the match continues. Optionally votes to drop a player whose
 *    connection stays down, which a Steam game otherwise waits for indefinitely.
 *
 * The launcher's helper loads this module and calls PawInstallQueued while the game's other threads are paused, as
 * for the lobby compatibility module. Every function called and every call or import redirected is checked against
 * the 1.3.72 bytes first; a feature whose bytes differ stays off. Game state is only changed from the game's main
 * thread (the message pump's PeekMessage, at most every 50 ms). Hooks only note events. No worker threads or sockets.
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

#define MAGIC 0x31524d50u /* "PMR1" */
enum { FEATURE_SAVE_COPIES = 1, FEATURE_AUTOSAVE = 2, FEATURE_DROP_RECOVERY = 4, FEATURE_LOBBY_STEP = 8,
       FEATURE_VOTE = 16, FEATURE_ALL = 31 };
typedef struct {
    uint32_t magic, features, mainThread, autosaveMinutes, voteAfterSeconds;
    wchar_t log[MAX_PATH];
} Config;

/* ------------------------------------------------------------------ 1.3.72 code (RVAs)
 * Patterns: hex bytes, "??" = an absolute operand that relocation changes. Each was re-read from the 1.3.72 image
 * at the address the 1.3.68 module used, found by its 1.3.68 pattern with call offsets ignored. */
typedef struct { const char *name; uint32_t rva; const char *bytes; } Signature;
enum {
    S_GAME_SAVE, S_CLIENT_SAVE_CALL, S_HOST_SAVE_CALL, S_BEGIN_SOLO_TASK, S_CREATE_SAVE_TASK, S_NEW, S_SAVE_TASK_CTOR,
    S_QUEUE_TASK, S_BLOCK_TASK, S_STRING_CTOR, S_STRING_RELEASE, S_USER_DATA, S_WANTED_PACKET, S_NETCONN_ADMIN,
    S_KICK_VOTE, S_LEAVE_DECREE, S_PLAYER_LEAVE, S_END_MISSION,
    S_GAME_TYPE, S_LOAD_SAVE_CTOR, S_LOAD_SAVE_ISSUE, S_COUNTDOWN_READ, S_SESSION_READ, S_FOR_EACH_CONNECTION,
    S_SCRIPT_READ, S_RUN_SCRIPT, S_SOURCE_WALK, S_SOURCE_STEP, S_SETTINGS_READ, S_SETTINGS_TYPE, S_BACKUP_MODE,
    S_WORLD_READ, S_MANAGER_READ, S_MESSAGE_PUMP, S_SAVE_TASK_SLOT, S_COUNT
};
static const Signature signatures[S_COUNT] = {
    {"KKC_Session::GameSave", 0x162a74, "b8 ?? ?? ?? ?? e8 5e eb 27 00 51 51 53 56 57 8b f9 8b 0d ?? ?? ?? ?? e8"},
    /* push eax; push ebx; call BeginSoloTask; push "NET: GameSave on client" */
    {"GameSave client call", 0x162b92, "50 53 e8 0d 9e fe ff 68 ?? ?? ?? ?? 68 ?? ?? ?? ?? 68 56 0c 00 00"},
    /* push esi; call; push 0; mov ecx, edi; call CreateSaveGameTask; mov ecx, [ebp-0xc] */
    {"GameSave host call", 0x162c30, "56 e8 80 13 ec ff 6a 00 8b cf e8 b7 7d 00 00 8b 4d f4"},
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
    {"net connection's admin connection", 0x15c551, "8b 81 a0 00 00 00 57 8b 7c 24 08 85"},
    {"CSDKickLaggingPlayers", 0xbc599, "55 8b ec 8b 4d 08 e8 ad c4 13 00 8b 0d ?? ?? ?? ?? f7 d8 1a c0 fe c0 88 45 08 ff 75 08 e8 fb e4 08 00"},
    /* push [ecx+8]; push [ecx+4]; mov ecx, [admin node]; call PlayerLeave; ret */
    {"player leave decree", 0x157080, "ff 71 08 ff 71 04 8b 0d ?? ?? ?? ?? e8 34 47 ff ff c3"},
    {"TGC_AdminNode::PlayerLeave", 0x14b7c5, "56 8b 74 24 08 57 8b f9 85 f6 75 23 68 ?? ?? ?? ?? 68 ?? ?? ?? ?? 68 d3 0c 00 00"},
    /* End Mission: mov ecx, app (+0xe) ... call game type object; cmp dword [eax+0xc], 0 */
    {"End Mission", 0xc3efb, "b8 ?? ?? ?? ?? e8 ?? ?? ?? ?? 83 ec 6c b9 ?? ?? ?? ?? 53 56 57 6a 06 5f e8 8c 82 07 00 6a 02 5b 83 78 0c 00"},
    {"game type object", 0x13c1a4, "8b 81 fc 00 00 00 c3"},
    {"KKC_HostSetLoadSavedGameOrder ctor", 0x121bd0, "55 8b ec 51 8b 45 08 56 57 8b f9 89 7d fc c7 07 ?? ?? ?? ??"},
    {"KKC_HostSetLoadSavedGameOrder issue", 0x122822, "b8 ?? ?? ?? ?? e8 ?? ?? ?? ?? 51 53 56 8b d9 c7 45 f0 00 00 00 00"},
    {"session countdown read", 0x162d04, "83 be 28 01 00 00 00 7e 2c"},
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
/* Call sites redirected (opcode E8) and the function each must call now. */
#define CLIENT_SAVE_CALL 0x162b94
#define HOST_SAVE_CALL 0x162c3a
#define LEAVE_DECREE_CALL 0x15708c
#define END_MISSION_SET_MAP_TYPE_CALL 0xc4143
#define END_MISSION_LAUNCH_CALL 0xc414d
#define SET_MAP_TYPE_ISSUE 0x8523a
#define LAUNCH_ISSUE 0x85139
#define PEEK_POINTER 0x5d4314 /* the pointer the message pump calls PeekMessage through (resolved on first use) */
/* Field offsets, each read by the 1.3.72 code in the signature named. */
#define SESSION_FILENAME 0x68    /* engine string; CreateSaveGameTask, GameSave */
#define SESSION_STATE 0xf0       /* 2 game, 4 film, 1 lobby; session read */
#define SESSION_NETWORKED 0x100  /* byte; End Mission */
#define SESSION_COUNTDOWN 0x128  /* int > 0 while the launch countdown runs; session countdown read */
#define SESSION_SAVE_TASK 0x134  /* the host's current save task (referenced); save task slot */
#define ADMIN_SERVER 0x2e4       /* GetWantedPacketType */
#define ADMIN_BACKUP_MODE 0x2f4  /* byte; SetBackupMode */
#define ADMIN_HOST_KIND 0x300    /* int; LeaveDecree sets 1 on a PC that takes over from a lost host */
#define NETNODE_CONNECTIONS 0x70 /* for each connection: {net connection, next} list */
#define NETCONN_ADMIN 0xa0
#define CONN_UP 0x98
#define APP_GAME_TYPE 0xfc
#define GAME_TYPE_VALUE 0x0c
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
typedef void (__attribute__((fastcall)) *PlayerLeave)(void *admin, void *edx, void *player, int reason);
typedef void (__attribute__((fastcall)) *BeginSoloTask)(void *admin, void *edx, int flag, void *node);
typedef void (__attribute__((fastcall)) *CreateSaveTask)(void *session, void *edx, int flag);
typedef void *(__cdecl *EngineNew)(size_t size);
typedef void *(__attribute__((fastcall)) *SaveTaskCtor)(void *self, void *edx, void *world, int isMap, void *name);
typedef void (__attribute__((fastcall)) *QueueTask)(void *manager, void *edx, void *task);
typedef void (__attribute__((fastcall)) *BlockTask)(void *task, void *edx);
typedef void *(__attribute__((fastcall)) *DeletingDtor)(void *self, void *edx, int flags);
/* returns a C++ bool: only AL is set, the rest of EAX is whatever the callee left there */
typedef unsigned char (__cdecl *IsFromResource)(void *settings);
typedef void (__cdecl *EndMissionFn)(void);
typedef void *(__attribute__((fastcall)) *OrderCtor)(void *self, void *edx, void *name);
typedef void (__attribute__((fastcall)) *OrderIssue)(void *self, void *edx, int target);
typedef BOOL (WINAPI *PeekFn)(MSG *message, HWND window, UINT first, UINT last, UINT remove);

static uintptr_t image;
static uint32_t imageSize;
static Config config;
static uint32_t features;
static int hostSavesOn; /* the host save hook is installed */
static uintptr_t consoleGlobal, sessionGlobal, adminGlobal, netNodeGlobal, appObject, worldGlobal, managerGlobal,
    personalGlobal;
static PeekFn originalPeek;
static volatile LONG inTick;
static DWORD lastTick;

__declspec(dllexport) volatile LONG PawRecoveryInstallStatus;  /* 0 not run, 1 installed, -1 failed */
__declspec(dllexport) volatile LONG PawRecoveryFeatures;       /* features actually on */
__declspec(dllexport) volatile LONG PawRecoveryCopies, PawRecoveryCopiesSkipped, PawRecoveryAutosaves,
    PawRecoveryDrops, PawRecoveryLobbySteps, PawRecoveryVotes, PawRecoveryTicks;

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

/* This PC hosts the running match: its admin node is the server and the session is in state 2 or 4. A PC that took
 * over from a lost host counts (it is the server now). */
static int host_of_running_match(const void *admin, const void *session) {
    int state;
    if (!admin || !session || FIELD(const void *, admin, ADMIN_SERVER) != admin) return 0;
    state = FIELD(int, session, SESSION_STATE);
    return state == 2 || state == 4;
}
/* Autosave: as above, not in backup mode, and someone else is connected. */
static int hosting_match(const void *admin, const void *session, const void *netNode) {
    return host_of_running_match(admin, session) && !FIELD(uint8_t, admin, ADMIN_BACKUP_MODE) && netNode &&
           FIELD(const void *, netNode, NETNODE_CONNECTIONS) != 0;
}
/* End Mission may run: the original host (not in backup mode, never took over) of a running networked multiplayer
 * match with no launch countdown, and the world exists. */
static int can_end_mission(const void *admin, const void *session, const void *world, int gameType) {
    if (!admin || !session || !world) return 0;
    if (FIELD(const void *, admin, ADMIN_SERVER) != admin || FIELD(uint8_t, admin, ADMIN_BACKUP_MODE) ||
        FIELD(int, admin, ADMIN_HOST_KIND) == 1)
        return 0;
    return FIELD(int, session, SESSION_STATE) == 2 && FIELD(uint8_t, session, SESSION_NETWORKED) &&
           FIELD(int, session, SESSION_COUNTDOWN) <= 0 && gameType == 1;
}
/* A connection of the net node to another PC is down (timed out, still listed). */
static int connection_down(const void *netNode) {
    const void *node;
    int guard = 0;
    if (!netNode) return 0;
    for (node = FIELD(const void *, netNode, NETNODE_CONNECTIONS); node && guard < 64; guard++) {
        const void *net = FIELD(const void *, node, 0);
        const void *conn = net ? FIELD(const void *, net, NETCONN_ADMIN) : 0;
        if (conn && !FIELD(uint8_t, conn, CONN_UP)) return 1;
        node = FIELD(const void *, node, 4);
    }
    return 0;
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

/* ------------------------------------------------------------------ game access (main thread only) */
static void *pointer_at(uintptr_t global) { return global ? *(void *const *)global : 0; }
static int game_type(void) {
    const void *type = appObject ? FIELD(const void *, appObject, APP_GAME_TYPE) : 0;
    return type ? FIELD(int, type, GAME_TYPE_VALUE) : -1;
}
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
/* Queues one script command (the game's console), as a "+command" on the command line does. */
static int run_script(const wchar_t *command) {
    void *console = pointer_at(consoleGlobal), *text = 0;
    if (!console) return 0;
    ((StringCtor)(image + 0x205de))(&text, 0, command); /* an engine string is one pointer to its text */
    ((RunScript)(image + 0x1f9963))(console, 0, &text, 0);
    ((StringRelease)(image + 0x21375))((char *)text - 0x10, 0);
    return 1;
}
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

/* ------------------------------------------------------------------ save copies and host saves */
static BeginSoloTask originalBeginSoloTask;
static CreateSaveTask originalCreateSaveTask;
/* Host saves are followed through the engine's own save task, not the file: CreateSaveGameTask stores it in the
 * session (+0x134, with a reference) and the session polls its status (+0x14: 2 finished, 3 failed). The hook (SIM
 * thread) takes one more reference and hands the task and its name over in a free slot; the main-thread tick reports
 * the result and releases the reference. */
#define SAVE_SLOTS 4
static struct { volatile LONG state; void *task; wchar_t name[MAX_PATH]; DWORD taken; } hostSaves[SAVE_SLOTS];
static wchar_t lastHostSave[MAX_PATH]; /* main thread: the latest host save that ended, and how */
static int lastHostSaveStatus;
__declspec(dllexport) volatile LONG PawRecoveryHostSavesUntracked;

static void release_task(void *task) {
    if (!InterlockedDecrement((volatile LONG *)((char *)task + TASK_REFS)))
        ((DeletingDtor)(*(void ***)task)[0])(task, 0, 1);
}
/* The walk the save task starts with, read-only, so a copy is only attempted where the game's own code would not
 * fault: 0 ready, 1 a resource without create settings, 2 a source whose header is not loaded, 3 a chain too long,
 * -1 nothing to save. A client whose lobby first picked a saved game and then a random map faulted here on 1.3.68. */
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
        if (!((IsFromResource)(image + 0x16bf8f))((void *)settings)) return 0;
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
/* "<what> "<name>": <bytes> bytes, lobby checksum <n>" for a save under the game's data folder (names relative to it:
 * no user name or Documents path in the log). */
static void log_checksum(const char *what, const wchar_t *relative) {
    wchar_t path[MAX_PATH];
    char a[MAX_PATH * 3];
    uint32_t checksum, bytes;
    if (on_disk(relative, path, MAX_PATH) && file_checksum(path, &checksum, &bytes))
        log_line("%s \"%s\": %lu bytes, lobby checksum %ld", what, utf8(relative, a, sizeof a), (unsigned long)bytes,
                 (long)(int32_t)checksum);
}
static void on_client_save(void) {
    wchar_t name[MAX_PATH], copy[MAX_PATH];
    char a[MAX_PATH * 3], b[MAX_PATH * 3];
    const void *session = pointer_at(sessionGlobal);
    int state = session ? FIELD(int, session, SESSION_STATE) : -1, ready, status, depth;
    DWORD start;
    session_file(session, name, MAX_PATH);
    if ((state != 2 && state != 4) || !copy_name(name, copy, MAX_PATH)) {
        InterlockedIncrement(&PawRecoveryCopiesSkipped);
        log_line("save copy skipped: file \"%s\", session state %d", utf8(name, a, sizeof a), state);
        return;
    }
    ready = sources_ready(&depth);
    if (ready) {
        static const char *const why[] = {"there is no world", "", "has no create settings", "is not loaded", "is too deep"};
        InterlockedIncrement(&PawRecoveryCopiesSkipped);
        log_line("save copy of \"%s\" skipped: the game's save code would stop at an assertion here (map source %d %s)",
                 utf8(name, a, sizeof a), depth, why[ready + 1]);
        return;
    }
    start = GetTickCount();
    status = write_copy(copy);
    if (status != 2) {
        InterlockedIncrement(&PawRecoveryCopiesSkipped);
        log_line("save copy of \"%s\" did not finish (status %d)", utf8(name, a, sizeof a), status);
        return;
    }
    InterlockedIncrement(&PawRecoveryCopies);
    log_line("save copy \"%s\" written in %lu ms", utf8(copy, b, sizeof b), (unsigned long)(GetTickCount() - start));
    log_checksum("save copy", copy);
}
/* The client branch's call of BeginSoloTask (thiscall, 2 arguments): unchanged, then the copy, on the thread that
 * applies the save order (SIM in 1.3.72). */
static void __attribute__((fastcall)) client_save_hook(void *admin, void *edx, int flag, void *node) {
    originalBeginSoloTask(admin, edx, flag, node);
    if (features & FEATURE_SAVE_COPIES) on_client_save();
}
/* The host branch's call of CreateSaveGameTask (thiscall, 1 argument): unchanged, then the task it stored is handed
 * to the tick. */
static void __attribute__((fastcall)) host_save_hook(void *session, void *edx, int flag) {
    void *task;
    int i;
    originalCreateSaveTask(session, edx, flag);
    task = session ? FIELD(void *, session, SESSION_SAVE_TASK) : 0;
    if (!task) return;
    for (i = 0; i < SAVE_SLOTS; i++)
        if (!InterlockedCompareExchange(&hostSaves[i].state, 1, 0)) {
            InterlockedIncrement((volatile LONG *)((char *)task + TASK_REFS));
            hostSaves[i].task = task;
            session_file(session, hostSaves[i].name, MAX_PATH);
            hostSaves[i].taken = 0;
            InterlockedExchange(&hostSaves[i].state, 2);
            return;
        }
    InterlockedIncrement(&PawRecoveryHostSavesUntracked);
}
static void host_save_tick(DWORD now) {
    char a[MAX_PATH * 3];
    int i, status;
    for (i = 0; i < SAVE_SLOTS; i++) {
        if (hostSaves[i].state != 2) continue;
        if (!hostSaves[i].taken) {
            hostSaves[i].taken = now ? now : 1;
            log_line("host save \"%s\" started", utf8(hostSaves[i].name, a, sizeof a));
        }
        status = FIELD(int, hostSaves[i].task, TASK_STATUS);
        if (status != 2 && status != 3 && now - hostSaves[i].taken < 120000) continue;
        wcscpy(lastHostSave, hostSaves[i].name);
        lastHostSaveStatus = status;
        if (status == 2) log_checksum("host save", hostSaves[i].name);
        else log_line("host save \"%s\" %s", utf8(hostSaves[i].name, a, sizeof a), status == 3 ? "failed" : "did not end in 2 minutes");
        release_task(hostSaves[i].task);
        hostSaves[i].task = 0;
        InterlockedExchange(&hostSaves[i].state, 0);
    }
}
static const int saveCopyNeeds[] = {S_GAME_SAVE, S_CLIENT_SAVE_CALL, S_BEGIN_SOLO_TASK, S_NEW, S_SAVE_TASK_CTOR,
    S_QUEUE_TASK, S_BLOCK_TASK, S_STRING_CTOR, S_STRING_RELEASE, S_SOURCE_WALK, S_SOURCE_STEP, S_SETTINGS_READ,
    S_SETTINGS_TYPE, S_WORLD_READ, S_MANAGER_READ, S_SESSION_READ, S_USER_DATA, -1};
static const int hostSaveNeeds[] = {S_GAME_SAVE, S_HOST_SAVE_CALL, S_CREATE_SAVE_TASK, S_SAVE_TASK_SLOT, -1};

/* ------------------------------------------------------------------ autosave */
static DWORD autosaveSince; /* when the hosted match was first seen, then the last save; 0 = none */
static int autosaveSlot;
static void autosave_tick(DWORD now) {
    static const wchar_t *const slots[] = {L"GameSave \"Save/MP Autosave A.RSG\"", L"GameSave \"Save/MP Autosave B.RSG\""};
    if (!hosting_match(pointer_at(adminGlobal), pointer_at(sessionGlobal), pointer_at(netNodeGlobal))) {
        if (autosaveSince) log_line("autosave: the match this PC hosted is over");
        autosaveSince = 0;
        return;
    }
    if (!autosaveSince) {
        autosaveSince = now ? now : 1;
        log_line("autosave: hosting a match; first save in %lu minute(s)", (unsigned long)config.autosaveMinutes);
        return;
    }
    if (now - autosaveSince < config.autosaveMinutes * 60000u || !run_script(slots[autosaveSlot])) return;
    InterlockedIncrement(&PawRecoveryAutosaves);
    log_line("autosave: saving \"MP Autosave %c\"", autosaveSlot ? 'B' : 'A');
    autosaveSince = now ? now : 1;
    autosaveSlot ^= 1;
}
static const int autosaveNeeds[] = {S_SESSION_READ, S_FOR_EACH_CONNECTION, S_WANTED_PACKET, S_GAME_SAVE,
    S_SCRIPT_READ, S_RUN_SCRIPT, S_STRING_CTOR, S_STRING_RELEASE, S_BACKUP_MODE, -1};

/* ------------------------------------------------------------------ drop recovery */
static PlayerLeave originalPlayerLeave;
static volatile LONG leavesPending, lastReason = -1;
/* After a leave: save, then (lobby step on, this PC the original host) wait for the save, take everyone to the lobby,
 * and select the save there. One step per tick, each at most 30 s.
 *  selecting:  a moment after arriving (an order sent in the same instant as the switch is refused "due to
 *              versioning": the switch resets the order version), send the save's selection;
 *  confirming: until the session plays that file, send it again every 2 s, at most 5 times; then move the host's own
 *              screen from the score screen to the lobby page (ScoringExit). */
enum { IDLE, SAVING, ENDING, SELECTING, CONFIRMING };
static int step;
static DWORD stepSince, sentAt, arrivedAt, downSince, lastVote;
static int sends, votes;
static wchar_t resumeSave[64];

/* The decree's call of PlayerLeave (thiscall, player and reason): the game's code runs first and unchanged; a leave
 * of a real player is only noted, and acted on from the main thread. */
static void __attribute__((fastcall)) leave_hook(void *admin, void *edx, void *player, int reason) {
    originalPlayerLeave(admin, edx, player, reason);
    if (player) {
        InterlockedExchange(&lastReason, reason);
        InterlockedIncrement(&leavesPending);
    }
}
static void go(int next, DWORD now) {
    step = next;
    stepSince = now;
}
static void on_leave(DWORD now) {
    LONG leaves = InterlockedExchange(&leavesPending, 0), reason = lastReason;
    const void *admin = pointer_at(adminGlobal), *session = pointer_at(sessionGlobal);
    wchar_t command[96];
    char a[96];
    SYSTEMTIME t;
    if (!host_of_running_match(admin, session)) return; /* not the host, or a leave from the lobby */
    if (step != IDLE) {
        log_line("drop recovery: %ld more player(s) left (reason %ld); already handling a leave", (long)leaves, (long)reason);
        return;
    }
    /* A new name per drop: a player who has an older file of the selected name would be asked to overwrite it. */
    GetLocalTime(&t);
    _snwprintf(resumeSave, 64, L"Save/MP Resume %04u-%02u-%02u %02u%02u%02u.RSG", t.wYear, t.wMonth, t.wDay, t.wHour,
               t.wMinute, t.wSecond);
    resumeSave[63] = 0;
    _snwprintf(command, 96, L"GameSave \"%ls\"", resumeSave);
    command[95] = 0;
    lastHostSave[0] = 0;
    if (!run_script(command)) {
        log_line("drop recovery: could not run GameSave");
        return;
    }
    run_script(L"GamePause 1");
    InterlockedIncrement(&PawRecoveryDrops);
    log_line("drop recovery: %ld player(s) left the match (reason %ld); saving \"%s\" and pausing", (long)leaves,
             (long)reason, utf8(resumeSave, a, sizeof a));
    if (!(features & FEATURE_LOBBY_STEP)) return;
    if (FIELD(int, admin, ADMIN_HOST_KIND) == 1) {
        log_line("drop recovery: this PC took over from the lost host; staying in the match (host again and load "
                 "\"MP Resume\" to continue with everyone)");
        return;
    }
    go(SAVING, now);
}
/* Votes to drop a player whose connection stays down while the game waits for them (backup mode). */
static void vote_tick(DWORD now) {
    const void *admin = pointer_at(adminGlobal), *session = pointer_at(sessionGlobal);
    if (!admin || !session || FIELD(int, session, SESSION_STATE) != 2 || !FIELD(uint8_t, admin, ADMIN_BACKUP_MODE) ||
        !connection_down(pointer_at(netNodeGlobal))) {
        downSince = 0;
        votes = 0;
        return;
    }
    if (!downSince) {
        downSince = now ? now : 1;
        log_line("drop recovery: a connection in the match is down; voting to drop that player in %lu s unless it "
                 "comes back", (unsigned long)config.voteAfterSeconds);
        return;
    }
    if (now - downSince < config.voteAfterSeconds * 1000u || votes >= 3 || (lastVote && now - lastVote < 30000)) return;
    /* The command votes the opposite of its argument: 0 = vote to drop. */
    if (!run_script(L"CSDKickLaggingPlayers 0")) return;
    votes++;
    lastVote = now ? now : 1;
    InterlockedIncrement(&PawRecoveryVotes);
    log_line("drop recovery: voted to drop the player who stopped answering %lu s ago (vote %d)",
             (unsigned long)((now - downSince) / 1000), votes);
}
/* What the lobby's "Load saved game" does: KKC_HostSetLoadSavedGameOrder with the file name, issued to the host. */
static void select_save(const wchar_t *name) {
    void *text = 0;
    struct { void *vtable; void *name; } order = {0, 0};
    ((StringCtor)(image + 0x205de))(&text, 0, name);
    ((OrderCtor)(image + 0x121bd0))(&order, 0, &text);
    ((OrderIssue)(image + 0x122822))(&order, 0, 0);
    if (order.name) ((StringRelease)(image + 0x21375))((char *)order.name - 0x10, 0);
    ((StringRelease)(image + 0x21375))((char *)text - 0x10, 0);
}
static void recovery_tick(DWORD now) {
    const void *admin, *session;
    wchar_t file[MAX_PATH];
    int state;
    if (leavesPending) on_leave(now);
    if (step == IDLE) {
        if (features & FEATURE_VOTE) vote_tick(now);
        return;
    }
    if (now - stepSince > 30000) {
        log_line("drop recovery: gave up waiting (step %d); the match stays as it is", step);
        go(IDLE, now);
        return;
    }
    admin = pointer_at(adminGlobal);
    session = pointer_at(sessionGlobal);
    if (!admin || !session) return;
    state = FIELD(int, session, SESSION_STATE);
    switch (step) {
    case SAVING: /* until the engine's save task for "MP Resume" has ended (host_save_tick) */
        if (_wcsicmp(lastHostSave, resumeSave)) return;
        if (lastHostSaveStatus != 2) {
            log_line("drop recovery: \"MP Resume\" was not saved (status %d); the match stays as it is", lastHostSaveStatus);
            go(IDLE, now);
            return;
        }
        go(ENDING, now);
        return;
    case ENDING:
        if (!can_end_mission(admin, session, pointer_at(worldGlobal), game_type())) {
            log_line("drop recovery: cannot take everyone to the lobby now (state %d, game type %d); staying", state,
                     game_type());
            go(IDLE, now);
            return;
        }
        log_line("drop recovery: \"MP Resume\" saved; taking everyone back to the lobby");
        ((EndMissionFn)(image + 0xc3efb))();
        arrivedAt = 0;
        go(SELECTING, now);
        return;
    case SELECTING:
        if (state != 1) { arrivedAt = 0; return; }
        if (!arrivedAt) arrivedAt = now ? now : 1;
        if (now - arrivedAt < 1500) return;
        sends = 0;
        sentAt = 0;
        go(CONFIRMING, now);
        return;
    case CONFIRMING:
        if (state != 1) {
            log_line("drop recovery: the session left the lobby (state %d) before \"MP Resume\" was selected", state);
            go(IDLE, now);
            return;
        }
        session_file(session, file, MAX_PATH);
        if (!_wcsicmp(file, resumeSave)) {
            InterlockedIncrement(&PawRecoveryLobbySteps);
            log_line("drop recovery: in the lobby with \"MP Resume\" selected after %d send(s): the player who dropped "
                     "can join again, then launch", sends);
            if (FIELD(int, admin, ADMIN_HOST_KIND) != 2) run_script(L"ScoringExit");
            go(IDLE, now);
            return;
        }
        if (sentAt && now - sentAt < 2000) return;
        if (sends >= 5) {
            log_line("drop recovery: the lobby did not take \"MP Resume\" after %d sends; choose it in Load saved game", sends);
            go(IDLE, now);
            return;
        }
        select_save(resumeSave);
        sends++;
        sentAt = now ? now : 1;
        return;
    default:
        go(IDLE, now);
    }
}
static const int dropNeeds[] = {S_LEAVE_DECREE, S_PLAYER_LEAVE, S_SESSION_READ, S_WANTED_PACKET, S_GAME_SAVE,
    S_SCRIPT_READ, S_RUN_SCRIPT, S_STRING_CTOR, S_STRING_RELEASE, S_USER_DATA, S_BACKUP_MODE, -1};
static const int lobbyNeeds[] = {S_END_MISSION, S_GAME_TYPE, S_LOAD_SAVE_CTOR, S_LOAD_SAVE_ISSUE, S_COUNTDOWN_READ,
    S_WORLD_READ, -1};
static const int voteNeeds[] = {S_FOR_EACH_CONNECTION, S_NETCONN_ADMIN, S_KICK_VOTE, -1};

/* ------------------------------------------------------------------ main-thread tick */
static void tick(void) {
    DWORD now = GetTickCount();
    InterlockedIncrement(&PawRecoveryTicks);
    if (hostSavesOn) host_save_tick(now);
    if (features & FEATURE_AUTOSAVE) autosave_tick(now);
    if (features & FEATURE_DROP_RECOVERY) recovery_tick(now);
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
    if (PawRecoveryInstallStatus) return 10;
    if (!cfg || cfg->magic != MAGIC || !cfg->mainThread || (cfg->features & ~FEATURE_ALL) || cfg->autosaveMinutes < 1 ||
        cfg->autosaveMinutes > 120 || cfg->voteAfterSeconds < 5 || cfg->voteAfterSeconds > 600)
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
            "multiplayer recovery"))
        return 21;
    if (!peek_entry(&originalPeek)) { log_line("off: the message pump's PeekMessage is not resolved to user32"); return 20; }
    consoleGlobal = operand(0x13df06, 2);
    sessionGlobal = operand(0x15bbe3, 1);
    adminGlobal = operand(0x150009, 5);
    netNodeGlobal = operand(0x150009, 0x21);
    personalGlobal = operand(0x1cb7fc, 2);
    worldGlobal = operand(0x16aa1c, 1);
    managerGlobal = operand(0x16aa67, 2);
    /* Host saves are followed for the log and for drop recovery's lobby step. */
    if ((cfg->features & (FEATURE_SAVE_COPIES | FEATURE_DROP_RECOVERY)) &&
        signatures_match(image, imageSize, hostSaveNeeds, "host save tracking")) {
        originalCreateSaveTask = (CreateSaveTask)(image + 0x16a9f6);
        hostSavesOn = redirect_call(image + HOST_SAVE_CALL, image + 0x16a9f6, host_save_hook);
    }
    if ((cfg->features & FEATURE_SAVE_COPIES) && signatures_match(image, imageSize, saveCopyNeeds, "save copies")) {
        originalBeginSoloTask = (BeginSoloTask)(image + 0x14c9a6);
        if (redirect_call(image + CLIENT_SAVE_CALL, image + 0x14c9a6, client_save_hook)) on |= FEATURE_SAVE_COPIES;
    }
    if ((cfg->features & FEATURE_AUTOSAVE) && signatures_match(image, imageSize, autosaveNeeds, "autosave"))
        on |= FEATURE_AUTOSAVE;
    if ((cfg->features & FEATURE_DROP_RECOVERY) && signatures_match(image, imageSize, dropNeeds, "drop recovery")) {
        originalPlayerLeave = (PlayerLeave)(image + 0x14b7c5);
        if (redirect_call(image + LEAVE_DECREE_CALL, image + 0x14b7c5, leave_hook)) {
            on |= FEATURE_DROP_RECOVERY;
            if ((cfg->features & FEATURE_LOBBY_STEP) && hostSavesOn && signatures_match(image, imageSize, lobbyNeeds, "lobby step") &&
                call_target(image + END_MISSION_SET_MAP_TYPE_CALL) == image + SET_MAP_TYPE_ISSUE &&
                call_target(image + END_MISSION_LAUNCH_CALL) == image + LAUNCH_ISSUE) {
                appObject = operand(0xc3efb, 0xe);
                on |= FEATURE_LOBBY_STEP;
            }
            if ((cfg->features & FEATURE_VOTE) && signatures_match(image, imageSize, voteNeeds, "vote"))
                on |= FEATURE_VOTE;
        }
    }
    features = on;
    if (!on || !write_peek(peek_hook)) {
        /* nothing to tick: put every redirected call back */
        if (on & FEATURE_SAVE_COPIES)
            redirect_call(image + CLIENT_SAVE_CALL, (uintptr_t)client_save_hook, (void *)(image + 0x14c9a6));
        if (hostSavesOn) redirect_call(image + HOST_SAVE_CALL, (uintptr_t)host_save_hook, (void *)(image + 0x16a9f6));
        hostSavesOn = 0;
        if (on & FEATURE_DROP_RECOVERY) {
            redirect_call(image + LEAVE_DECREE_CALL, (uintptr_t)leave_hook, (void *)(image + 0x14b7c5));
        }
        features = 0;
        PawRecoveryInstallStatus = -1;
        log_line("off: no feature could be installed");
        return 40;
    }
    PawRecoveryFeatures = (LONG)on;
    PawRecoveryInstallStatus = 1;
    log_line("installed for k2.exe 1.3.72: save copies %s, autosave %s (%lu min), drop recovery %s, lobby step %s, "
             "vote %s (%lu s)", on & FEATURE_SAVE_COPIES ? "on" : "off", on & FEATURE_AUTOSAVE ? "on" : "off",
             (unsigned long)config.autosaveMinutes, on & FEATURE_DROP_RECOVERY ? "on" : "off",
             on & FEATURE_LOBBY_STEP ? "on" : "off", on & FEATURE_VOTE ? "on" : "off",
             (unsigned long)config.voteAfterSeconds);
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
static unsigned char admin[0x400], session[0x200], netNode[0x80], node1[8], node2[8], net1[0xb0], net2[0xb0],
    conn1[0xa0], conn2[0xa0], world[0x40], typeObject[0x10];
/* The game calls each hook as a thiscall: ECX = object, arguments pushed right to left, callee pops them. These
 * stand in for the original functions and record what reached them. */
static void *seenSelf, *seenFirst;
static int seenSecond, seenCalls;
static void __attribute__((fastcall)) fake_two(void *self, void *edx, void *first, int second) {
    seenSelf = self; seenFirst = first; seenSecond = second; seenCalls++;
}
static void __attribute__((fastcall)) fake_flag_node(void *self, void *edx, int flag, void *node) {
    seenSelf = self; seenSecond = flag; seenFirst = node; seenCalls++;
}
static void __attribute__((fastcall)) fake_one(void *self, void *edx, int flag) {
    seenSelf = self; seenSecond = flag; seenCalls++;
}
/* thiscall with two (or one) stack arguments; 1 when ESP, EBX, ESI, EDI and EBP are the same afterwards. The values
 * go to static memory: EAX, ECX and EDX belong to the callee. */
uint32_t pawRegsBefore[5], pawRegsAfter[5];
#define SAVE_REGS(to) "movl %%esp, " to "\n\tmovl %%ebx, " to "+4\n\tmovl %%esi, " to "+8\n\tmovl %%edi, " to "+12\n\tmovl %%ebp, " to "+16\n\t"
static int thiscall2(void *fn, void *self, uint32_t first, uint32_t second) {
    __asm__ volatile(SAVE_REGS("pawRegsBefore") "pushl %2\n\tpushl %1\n\tmovl %0, %%ecx\n\tcall *%3\n\t" SAVE_REGS("pawRegsAfter")
                     : : "m"(self), "m"(first), "m"(second), "m"(fn) : "eax", "ecx", "edx", "memory");
    return !memcmp(pawRegsBefore, pawRegsAfter, sizeof pawRegsBefore);
}
static int thiscall1(void *fn, void *self, uint32_t first) {
    __asm__ volatile(SAVE_REGS("pawRegsBefore") "pushl %1\n\tmovl %0, %%ecx\n\tcall *%2\n\t" SAVE_REGS("pawRegsAfter")
                     : : "m"(self), "m"(first), "m"(fn) : "eax", "ecx", "edx", "memory");
    return !memcmp(pawRegsBefore, pawRegsAfter, sizeof pawRegsBefore);
}
static void reset(void) {
    memset(admin, 0, sizeof admin); memset(session, 0, sizeof session); memset(netNode, 0, sizeof netNode);
    *(void **)(admin + ADMIN_SERVER) = admin;
    *(int *)(session + SESSION_STATE) = 2;
    session[SESSION_NETWORKED] = 1;
    *(void **)(netNode + NETNODE_CONNECTIONS) = node1;
    *(void **)node1 = net1; *(void **)(node1 + 4) = node2; *(void **)node2 = net2; *(void **)(node2 + 4) = 0;
    *(void **)(net1 + NETCONN_ADMIN) = conn1; *(void **)(net2 + NETCONN_ADMIN) = conn2;
    conn1[CONN_UP] = 1; conn2[CONN_UP] = 1;
}
int main(int argc, char **argv) {
    wchar_t out[MAX_PATH];
    unsigned i;
    reset();
    check(host_of_running_match(admin, session), "host of a running match");
    *(int *)(session + SESSION_STATE) = 4; check(host_of_running_match(admin, session), "host of a film");
    *(int *)(session + SESSION_STATE) = 1; check(!host_of_running_match(admin, session), "lobby is not a match");
    reset(); *(void **)(admin + ADMIN_SERVER) = session; check(!host_of_running_match(admin, session), "client");
    check(!host_of_running_match(0, session) && !host_of_running_match(admin, 0), "missing objects");
    reset(); check(hosting_match(admin, session, netNode), "hosting with players");
    admin[ADMIN_BACKUP_MODE] = 1; check(!hosting_match(admin, session, netNode), "no autosave in backup mode");
    reset(); *(void **)(netNode + NETNODE_CONNECTIONS) = 0; check(!hosting_match(admin, session, netNode), "alone");
    reset(); check(can_end_mission(admin, session, world, 1), "End Mission allowed");
    check(!can_end_mission(admin, session, world, 0), "not in a campaign");
    check(!can_end_mission(admin, session, 0, 1), "no world");
    admin[ADMIN_BACKUP_MODE] = 1; check(!can_end_mission(admin, session, world, 1), "not in backup mode");
    reset(); *(int *)(admin + ADMIN_HOST_KIND) = 1; check(!can_end_mission(admin, session, world, 1), "not after takeover");
    reset(); *(int *)(session + SESSION_COUNTDOWN) = 3; check(!can_end_mission(admin, session, world, 1), "countdown");
    reset(); session[SESSION_NETWORKED] = 0; check(!can_end_mission(admin, session, world, 1), "single player");
    reset(); *(int *)(session + SESSION_STATE) = 4; check(!can_end_mission(admin, session, world, 1), "film");
    reset(); check(!connection_down(netNode), "all connections up");
    conn2[CONN_UP] = 0; check(connection_down(netNode), "second connection down");
    *(void **)(node1 + 4) = node1; check(!connection_down(netNode), "a looped list ends after 64 entries");
    reset(); *(void **)(net1 + NETCONN_ADMIN) = 0; check(!connection_down(netNode), "net connection without admin");
    check(copy_name(L"Save/X.RSG", out, MAX_PATH) && !wcscmp(out, L"Save/X (copy).RSG"), "copy name");
    check(copy_name(L"Save/MP Resume 2026-10-13 210102.rsg", out, MAX_PATH) && !wcscmp(out, L"Save/MP Resume 2026-10-13 210102 (copy).rsg"), "copy keeps case");
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
    /* hooks under the game's calling convention: arguments reach the original, the stack and saved registers are
     * balanced, and the event is noted */
    originalPlayerLeave = (PlayerLeave)fake_two; seenCalls = 0; leavesPending = 0;
    check(thiscall2((void *)leave_hook, admin, (uint32_t)(uintptr_t)session, 5), "leave hook keeps the stack");
    check(seenCalls == 1 && seenSelf == admin && seenFirst == session && seenSecond == 5 && leavesPending == 1 &&
          lastReason == 5, "leave hook passes the leave on and notes it");
    check(thiscall2((void *)leave_hook, admin, 0, 6) && leavesPending == 1, "a leave without a player is not noted");
    originalBeginSoloTask = (BeginSoloTask)fake_flag_node; features = 0;
    check(thiscall2((void *)client_save_hook, admin, 1, (uint32_t)(uintptr_t)world), "client save hook keeps the stack");
    check(seenSelf == admin && seenSecond == 1 && seenFirst == world, "client save hook passes the call on");
    {
        static unsigned char task[0x2c];
        originalCreateSaveTask = (CreateSaveTask)fake_one;
        *(void **)(session + SESSION_SAVE_TASK) = task; *(LONG *)(task + TASK_REFS) = 1; *(int *)(task + TASK_STATUS) = 1;
        check(thiscall1((void *)host_save_hook, session, 0) && seenSelf == session && seenSecond == 0,
              "host save hook keeps the stack and passes the call on");
        check(hostSaves[0].state == 2 && hostSaves[0].task == task && *(LONG *)(task + TASK_REFS) == 2,
              "host save hook takes a reference on the session's task and hands it over");
        host_save_tick(1000);
        check(hostSaves[0].state == 2 && *(LONG *)(task + TASK_REFS) == 2, "a running save task is kept");
        *(int *)(task + TASK_STATUS) = 2;
        host_save_tick(1100);
        check(hostSaves[0].state == 0 && *(LONG *)(task + TASK_REFS) == 1 && lastHostSaveStatus == 2,
              "a finished save task is reported and its reference released");
        *(void **)(session + SESSION_SAVE_TASK) = 0;
        check(thiscall1((void *)host_save_hook, session, 0) && hostSaves[0].state == 0, "no task, nothing handed over");
    }
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
        {
            const uint32_t sites[][2] = {{CLIENT_SAVE_CALL, 0x14c9a6}, {HOST_SAVE_CALL, 0x16a9f6}, {LEAVE_DECREE_CALL, 0x14b7c5},
                {END_MISSION_SET_MAP_TYPE_CALL, SET_MAP_TYPE_ISSUE},
                {END_MISSION_LAUNCH_CALL, LAUNCH_ISSUE}};
            for (i = 0; i < sizeof sites / sizeof sites[0]; i++)
                check(call_target((uintptr_t)mapped + sites[i][0]) == (uintptr_t)mapped + sites[i][1], "call site target");
        }
    }
    printf("%s %u multiplayer recovery checks%s\n", failed ? "FAIL" : "PASS", checks, argc == 2 ? " (with the 1.3.72 image)" : "");
    return failed;
}
#endif
