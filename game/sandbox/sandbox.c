/* Kohan II sandbox (single player, game 1.3.68 build 24805354 only).
 *
 * A stand-in shfolder.dll for a separate sandbox copy of the game. k2.exe loads shfolder.dll from its own folder to
 * find the Documents folder; on that first call (game code, after the protector has unpacked the exe) this DLL checks
 * the exe's bytes and, only when they are 1.3.68's, installs:
 *
 *  - Computer players off: the two strategic AI fiber loops get a wrapper around their per-kingdom think
 *    (GoalEngineThink, RAI think). With the sandbox active the wrapper clears the AI player's active byte for the
 *    call, so the game takes its own "skip" path (the one it uses for defeated kingdoms) and only reschedules.
 *    Company/unit AI (guarding, auto-engage) is untouched.
 *  - Control any kingdom: selecting any unit or building of a player kingdom makes that kingdom yours by pointing the
 *    local session player at it (+0x28, the field both the interface and the order checks read). Alt+1..8 does
 *    the same by list position.
 *  - A kingdom list at the top left: colour swatch, team, name and economy of every player kingdom, in slot order.
 *    Drawn with D3DX on the game's device (Present hook).
 *
 * Active only while exactly one human session player exists (single player). Every other case: the game as shipped.
 * Hotkeys: Alt+1..8 take control, Ctrl+Shift+A computer players on/off, Ctrl+Shift+H list on/off.
 * Log: sandbox.log next to k2.exe. */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>
#include <stdio.h>
#include <stdarg.h>

typedef uint32_t U;
#define P(a, o) (*(U *)((U)(a) + (o)))
#define B(a, o) (*(unsigned char *)((U)(a) + (o)))
#define F(a, o) (*(float *)((U)(a) + (o)))
#define FAST __attribute__((fastcall))

/* ---- 1.3.68 (build 24805354) layout, RVAs from image base ---- */
#define RVA_RESOURCES 0x5d7fac   /* resource table: defs +0x34, count +0x38 */
#define RVA_WORLD 0x5d7fb0       /* world: game time float +0xe8 */
#define RVA_UI 0x5d7fb4          /* interface: selection manager +0x13c */
#define RVA_MANAGER 0x5d7fb8     /* player manager (local kingdom getter's this) */
#define RVA_SAI 0x5d7fc0         /* strategic AI: players +0x6c, count +0x70; player +8 = kingdom */
#define RVA_SESSION 0x5d7fdc     /* session: state +0xf0, session players list +0xc8 (node: player, next) */
#define RVA_LOCAL 0x5d7fe4       /* +0xc = local session player; player +0xc type (0 human), +0x28 kingdom */
#define RVA_REGISTRY 0x5d372c    /* object registry: +0x20004 + 4 * (id & 0xffff) */
#define RVA_LOCAL_KINGDOM 0x2bdf23 /* thiscall(manager) -> kingdom the interface shows as yours */
#define RVA_SEL_OWNER 0x2e0970   /* thiscall(selection entry) -> owning kingdom of a live actor, else 0 */
#define RVA_GOAL_THINK 0x1ee51b  /* KKC_SAIPlayer::GoalEngineThink */
#define RVA_RAI_THINK 0x1ee86f   /* KKC_SAIPlayer RAI think */
#define RVA_GOAL_PUSH 0x1e154a   /* push GoalEngineThink, in the goal-engine fiber loop */
#define RVA_RAI_PUSH 0x1e1590    /* push RAI think, in the RAI fiber loop */
#define RVA_D3D_IAT 0x44d390     /* import slot of d3d9!Direct3DCreate9 */
/* kingdom: +0x14 id, +0x190 number, +0x1a8 production[], +0x1b4 capacity[], +0x1c0 consumption[], +0x1cc stock[],
 * +0x1f4 colour (rgb floats +0x18), +0x1f8 team. resource def: +0x18 kind (0 stock, 1 rate, 2 limit), +0x2c hidden,
 * +0x30 paired def (+0xc its index) */

static uintptr_t image;
static HMODULE real;
static volatile LONG installTried;
static int installed;
static volatile LONG aiOff = 1;  /* computer players off (Ctrl+Shift+A) */
static int listShown = 1;        /* Ctrl+Shift+H */
static volatile LONG thinkSkipped, thinkPassed;
static int diag;                 /* K2SB_DIAG=1: one-time dump of kingdoms and resources */
static int bisect;               /* K2SB_BISECT: 1 no drawing, 2 no per-frame work at all (test runs) */
static int testSwitch;           /* K2SB_TEST_SWITCH=n: take control of list entry n 20 s into the match */
static int shots[8], shotCount;  /* K2SB_SHOTS=t1,t2,...: save the back buffer (list included) at these game seconds */
static wchar_t logPath[MAX_PATH];

static void sblog(const char *fmt, ...) {
    char line[1024];
    int n;
    va_list ap;
    SYSTEMTIME t;
    HANDLE h;
    DWORD w;
    GetLocalTime(&t);
    n = _snprintf(line, sizeof line, "%02d:%02d:%02d.%03d ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    va_start(ap, fmt);
    n += _vsnprintf(line + n, sizeof line - n - 2, fmt, ap);
    va_end(ap);
    if (n < 0 || n > (int)sizeof line - 2) n = sizeof line - 2;
    line[n++] = '\r';
    line[n++] = '\n';
    h = CreateFileW(logPath, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE, 0, OPEN_ALWAYS, 0, 0);
    if (h == INVALID_HANDLE_VALUE) return;
    WriteFile(h, line, n, &w, 0);
    CloseHandle(h);
}

/* ---- game state ---- */
typedef U(FAST *Get)(U self, U edx);
static int readable(U p, U n) {
    MEMORY_BASIC_INFORMATION m;
    if (p < 0x10000 || !VirtualQuery((void *)p, &m, sizeof m)) return 0;
    if (m.State != MEM_COMMIT || (m.Protect & (PAGE_NOACCESS | PAGE_GUARD))) return 0;
    return p + n <= (U)m.BaseAddress + m.RegionSize;
}
static int live(U k) {
    U reg = P(image, RVA_REGISTRY), id;
    if (!reg || !k) return 0;
    id = P(k, 0x14);
    return id && P(reg, 0x20004 + 4 * (id & 0xffff)) == k;
}
static U knum(U k) { return k ? P(k, 0x190) : 0; }
static U local_player(void) {
    U h = P(image, RVA_LOCAL);
    return h ? P(h, 0xc) : 0;
}
/* Exactly one human session player: single player. Reads the session list (static in single player). */
static int single_player(void) {
    U s = P(image, RVA_SESSION), node, humans = 0, i = 0;
    if (!s) return 0;
    for (node = P(s, 0xc8); node && i < 64; node = P(node, 4), i++) {
        U p = P(node, 0);
        if (p && B(p, 0xc) == 0) humans++;
    }
    return humans == 1 && i < 64;
}

typedef struct { U k; U number; U team; DWORD color; wchar_t name[40]; } Kingdom; /* name, colour: refreshed every 2 s */
static Kingdom kingdoms[16];
static int kingdomCount;
static U teams[16];
static int teamCount;
/* Player kingdoms in kingdom-number order (slot order). Gathered from the session players and the strategic AI's
 * players, and remembered for the whole match: after a switch no session player points at the kingdom left behind. */
static U listWorld;
static DWORD kingdom_color(U k);
static const wchar_t *engine_text(U str, const wchar_t *fallback);
static wchar_t labels[16][24]; /* resource names, refreshed with the kingdoms */
static void refresh_details(void) {
    U table = P(image, RVA_RESOURCES), defs, count, i;
    int k;
    for (k = 0; k < kingdomCount; k++) {
        wcsncpy(kingdoms[k].name, engine_text(P(kingdoms[k].k, 0x1a0), L"?"), 39);
        kingdoms[k].name[39] = 0;
        kingdoms[k].color = kingdom_color(kingdoms[k].k);
    }
    if (!table || !(defs = P(table, 0x34)) || (count = P(table, 0x38)) > 16) return;
    for (i = 0; i < count; i++) {
        U def = P(defs, 4 * i);
        wcsncpy(labels[i], def ? engine_text(P(def, 0x10), L"?") : L"?", 23);
        labels[i][23] = 0;
    }
}
static void collect(U world) {
    U s = P(image, RVA_SESSION), sai = P(image, RVA_SAI), node, i, j, n = 0;
    U found[32];
    int keep = 0;
    if (world != listWorld) {
        listWorld = world;
        kingdomCount = 0;
    }
    if (s)
        for (node = P(s, 0xc8), i = 0; node && i < 64 && n < 32; node = P(node, 4), i++) {
            U p = P(node, 0);
            if (p) found[n++] = P(p, 0x28);
        }
    if (sai && P(sai, 0x70) <= 32 && P(sai, 0x6c))
        for (i = 0; i < P(sai, 0x70) && n < 32; i++) {
            U ap = P(P(sai, 0x6c), 4 * i);
            if (ap) found[n++] = P(ap, 8);
        }
    for (i = 0; i < (U)kingdomCount; i++) /* drop destroyed kingdoms */
        if (live(kingdoms[i].k)) kingdoms[keep++] = kingdoms[i];
    kingdomCount = keep;
    for (i = 0; i < n; i++) {
        U k = found[i];
        int dup = 0;
        if (!live(k)) continue;
        for (j = 0; j < (U)kingdomCount; j++)
            if (kingdoms[j].k == k) dup = 1;
        if (dup || kingdomCount >= 16) continue;
        kingdoms[kingdomCount].k = k;
        kingdomCount++;
    }
    for (i = 0; i < (U)kingdomCount; i++) {
        kingdoms[i].number = P(kingdoms[i].k, 0x190);
        kingdoms[i].team = P(kingdoms[i].k, 0x1f8);
    }
    {
        /* No memory queries per frame: they starved the simulation thread's start (2026-10-16 test runs). */
        static DWORD next;
        static int lastCount;
        DWORD now = GetTickCount();
        if (now >= next || kingdomCount != lastCount) {
            next = now + 2000;
            lastCount = kingdomCount;
            refresh_details();
        }
    }
    for (i = 1; i < (U)kingdomCount; i++)
        for (j = i; j > 0 && kingdoms[j - 1].number > kingdoms[j].number; j--) {
            Kingdom t = kingdoms[j];
            kingdoms[j] = kingdoms[j - 1];
            kingdoms[j - 1] = t;
        }
    /* Team numbers by first appearance in slot order (0 = no team). */
    teamCount = 0;
    for (i = 0; i < (U)kingdomCount; i++) {
        U t = kingdoms[i].team;
        int seen = 0;
        if (!t) continue;
        for (j = 0; j < (U)teamCount; j++)
            if (teams[j] == t) seen = 1;
        if (!seen && teamCount < 16) teams[teamCount++] = t;
    }
}
static int team_number(U t) {
    int i;
    for (i = 0; i < teamCount; i++)
        if (teams[i] == t) return i + 1;
    return 0;
}
static int is_player_kingdom(U k) {
    int i;
    for (i = 0; i < kingdomCount; i++)
        if (kingdoms[i].k == k) return 1;
    return 0;
}
static U selected_owner(void) {
    U ui = P(image, RVA_UI), m, node, e;
    if (!ui || !(m = P(ui, 0x13c)) || !(node = P(m, 8)) || !(e = P(node, 0))) return 0;
    return ((Get)(image + RVA_SEL_OWNER))(e, 0);
}
static void take_control(U k, const char *why) {
    U pl = local_player(), old;
    if (!pl || !live(k) || (old = P(pl, 0x28)) == k) return;
    P(pl, 0x28) = k;
    sblog("control: kingdom #%u -> #%u (%s); interface now shows #%u", knum(old), knum(k), why,
         knum(((Get)(image + RVA_LOCAL_KINGDOM))(P(image, RVA_MANAGER), 0)));
}

/* ---- computer players off ---- */
static void think(U player, U rva, volatile LONG *count) {
    int cleared = 0;
    unsigned char was = B(player, 4);
    if (aiOff && was && single_player()) {
        B(player, 4) = 0; /* the game's own skip path: reschedule only */
        cleared = 1;
        InterlockedIncrement(&thinkSkipped);
    } else
        InterlockedIncrement(&thinkPassed);
    ((Get)(image + rva))(player, 0);
    if (cleared && B(player, 4) == 0) B(player, 4) = was;
}
static void FAST goal_think(U player, U edx) { think(player, RVA_GOAL_THINK, 0); }
static void FAST rai_think(U player, U edx) { think(player, RVA_RAI_THINK, 0); }

/* ---- overlay (D3D9 / D3DX through vtables, no SDK headers) ---- */
typedef struct { LONG x1, y1, x2, y2; } D3DRECT_;
typedef struct { DWORD X, Y, Width, Height; float MinZ, MaxZ; } VIEWPORT;
typedef void *(WINAPI *Create9)(UINT);
typedef HRESULT(WINAPI *CreateDeviceFn)(void *, UINT, DWORD, HWND, DWORD, void *, void **);
typedef HRESULT(WINAPI *PresentFn)(void *, const RECT *, const RECT *, HWND, const void *);
typedef HRESULT(WINAPI *ResetFn)(void *, void *);
typedef HRESULT(WINAPI *Method0)(void *);
typedef HRESULT(WINAPI *ClearFn)(void *, DWORD, const D3DRECT_ *, DWORD, DWORD, float, DWORD);
typedef HRESULT(WINAPI *ViewportFn)(void *, VIEWPORT *);
typedef HRESULT(WINAPI *SetRSFn)(void *, DWORD, DWORD);
typedef HRESULT(WINAPI *GetRSFn)(void *, DWORD, DWORD *);
typedef HRESULT(WINAPI *CreateFontFn)(void *, INT, UINT, UINT, UINT, BOOL, DWORD, DWORD, DWORD, DWORD, const wchar_t *,
                                      void **);
typedef INT(WINAPI *DrawTextFn)(void *, void *, const wchar_t *, INT, RECT *, DWORD, DWORD);
#define VT(o, i) ((*(void ***)(o))[i])

static Create9 realCreate9;
static CreateDeviceFn realCreateDevice;
static void *font;
static int fontHeight;
static U backW, backH;
static DWORD presentThread;

static int patch_ptr(void **slot, void *value, void **old) {
    DWORD prot, ignored;
    if (!VirtualProtect(slot, 4, PAGE_EXECUTE_READWRITE, &prot)) return 0;
    if (old) *old = *slot;
    *slot = value;
    VirtualProtect(slot, 4, prot, &ignored);
    return 1;
}
/* The module holding code address p, for the log ("?" when none). */
static const wchar_t *owner(void *p, wchar_t *name) {
    HMODULE m;
    wchar_t *slash;
    if (!p || !GetModuleHandleExW(0x4 | 0x2, (LPCWSTR)p, &m) || !GetModuleFileNameW(m, name, MAX_PATH)) return L"?";
    slash = wcsrchr(name, L'\\');
    return slash ? slash + 1 : name;
}
static void font_release(void) {
    if (font) ((Method0)VT(font, 2))(font);
    font = 0;
}
static void text(int x, int y, const wchar_t *s, DWORD color) {
    RECT r;
    DrawTextFn draw = (DrawTextFn)VT(font, 15);
    r.left = x + 1; r.top = y + 1; r.right = x + 2000; r.bottom = y + 200;
    draw(font, 0, s, -1, &r, 0x100 /* DT_NOCLIP */, 0xc0000000);
    r.left = x; r.top = y;
    draw(font, 0, s, -1, &r, 0x100, color);
}
static void fill(void *dev, int x, int y, int w, int h, DWORD color) {
    D3DRECT_ r;
    r.x1 = x; r.y1 = y; r.x2 = x + w; r.y2 = y + h;
    ((ClearFn)VT(dev, 43))(dev, 1, &r, 1 /* D3DCLEAR_TARGET */, color, 1.0f, 0);
}
static DWORD to_argb(float r, float g, float b) {
    int R = (int)(r * 255.0f + 0.5f), G = (int)(g * 255.0f + 0.5f), Bl = (int)(b * 255.0f + 0.5f);
    if (R < 0) R = 0; if (R > 255) R = 255;
    if (G < 0) G = 0; if (G > 255) G = 255;
    if (Bl < 0) Bl = 0; if (Bl > 255) Bl = 255;
    return 0xff000000u | (R << 16) | (G << 8) | Bl;
}
static DWORD kingdom_color(U k) {
    U c = P(k, 0x1f4);
    float r, g, b;
    if (!c || !readable(c + 0x18, 12)) return 0xff808080u;
    r = F(c, 0x18); g = F(c, 0x1c); b = F(c, 0x20);
    if (!(r >= 0 && r <= 1.01f && g >= 0 && g <= 1.01f && b >= 0 && b <= 1.01f)) return 0xff808080u;
    return to_argb(r, g, b);
}
static void number(wchar_t *out, int n, float v, int sign) {
    float a = v < 0 ? -v : v;
    if (a < 0.05f) v = 0;
    if (a >= 100 || (a - (int)a) < 0.05f || (a - (int)a) > 0.95f)
        _snwprintf(out, n, sign ? L"%+.0f" : L"%.0f", v);
    else
        _snwprintf(out, n, sign ? L"%+.1f" : L"%.1f", v);
}
/* Engine strings: a pointer to UTF-16 text with the length 12 bytes before it. */
static const wchar_t *engine_text(U str, const wchar_t *fallback) {
    U len;
    if (!readable(str - 12, 12) || (len = P(str, -12)) == 0 || len > 48 || !readable(str, len * 2 + 2)) return fallback;
    return (const wchar_t *)str;
}
/* One kingdom's economy, as the top bar computes it: net = production - consumption; kind 0 also shows the stock,
 * kind 2 (limits such as kingdom points) shows used / capacity of its paired resource. */
static void economy(U k, wchar_t *out, int n) {
    U table = P(image, RVA_RESOURCES), defs, count, i, len = 0;
    U prod = P(k, 0x1a8), cons = P(k, 0x1c0), stock = P(k, 0x1cc);
    out[0] = 0;
    if (!table || !(defs = P(table, 0x34)) || (count = P(table, 0x38)) == 0 || count > 16 || !prod || !cons || !stock)
        return;
    for (i = 0; i < count && len < (U)n - 64; i++) {
        U def = P(defs, 4 * i), kind;
        wchar_t a[24], b[24];
        if (!def || B(def, 0x2c)) continue;
        kind = P(def, 0x18);
        if (kind == 2) {
            U pair = P(def, 0x30), pi = pair ? P(pair, 0xc) : 99;
            if (pi >= count) continue;
            number(a, 24, F(cons, 4 * i) - F(prod, 4 * i), 0);
            number(b, 24, F(prod, 4 * pi) - F(cons, 4 * pi), 0);
            len += _snwprintf(out + len, n - len, L"   %s %s/%s", labels[i], a, b);
        } else if (kind == 0) {
            number(a, 24, F(stock, 4 * i), 0);
            number(b, 24, F(prod, 4 * i) - F(cons, 4 * i), 1);
            len += _snwprintf(out + len, n - len, L"   %s %s (%s)", labels[i], a, b);
        } else {
            number(a, 24, F(prod, 4 * i) - F(cons, 4 * i), 1);
            len += _snwprintf(out + len, n - len, L"   %s %s", labels[i], a);
        }
    }
}
static void draw_list(void *dev) {
    VIEWPORT saved, full;
    DWORD scissor = 0;
    int i, x = (int)(backH * 0.105f), y = (int)(backH * 0.075f), line = fontHeight + fontHeight / 4;
    U mine = P(local_player(), 0x28);
    wchar_t row[512], eco[400];
    if (!kingdomCount) return;
    ((ViewportFn)VT(dev, 48))(dev, &saved);
    full.X = 0; full.Y = 0; full.Width = backW; full.Height = backH; full.MinZ = 0; full.MaxZ = 1;
    ((ViewportFn)VT(dev, 47))(dev, &full);
    ((GetRSFn)VT(dev, 58))(dev, 174 /* D3DRS_SCISSORTESTENABLE */, &scissor);
    ((SetRSFn)VT(dev, 57))(dev, 174, 0);
    for (i = 0; i < kingdomCount; i++) {
        U k = kingdoms[i].k;
        int t = team_number(kingdoms[i].team), yy = y + i * line, sw = fontHeight - 2;
        if (k == mine) fill(dev, x - 4, yy - 1, 3, fontHeight + 2, 0xffffd700u);
        fill(dev, x, yy + 1, sw, sw, 0xff000000u);
        fill(dev, x + 1, yy + 2, sw - 2, sw - 2, kingdoms[i].color);
        economy(k, eco, 400);
        if (t)
            _snwprintf(row, 512, L"%d  T%d  %s%s", i + 1, t, kingdoms[i].name, eco);
        else
            _snwprintf(row, 512, L"%d  --  %s%s", i + 1, kingdoms[i].name, eco);
        text(x + sw + 6, yy, row, k == mine ? 0xffffe680u : 0xffffffffu);
    }
    _snwprintf(row, 512, L"Sandbox: click any unit to control its kingdom (or Alt+1-%d)   computer players %s (Ctrl+Shift+A)   hide: Ctrl+Shift+H",
               kingdomCount, aiOff ? L"off" : L"ON");
    text(x, y + kingdomCount * line + 2, row, 0xffc8c8c8u);
    ((SetRSFn)VT(dev, 57))(dev, 174, scissor);
    ((ViewportFn)VT(dev, 47))(dev, &saved);
}

/* ---- per frame (the game's render thread = its interface thread) ---- */
static int keyDown[256];
static int pressed(int vk) {
    int down = (GetAsyncKeyState(vk) & 0x8000) != 0, was = keyDown[vk];
    keyDown[vk] = down;
    return down && !was;
}
static int foreground(void) {
    DWORD pid = 0;
    HWND w = GetForegroundWindow();
    if (w) GetWindowThreadProcessId(w, &pid);
    return pid == GetCurrentProcessId();
}
typedef HRESULT(WINAPI *BackBufferFn)(void *, UINT, UINT, DWORD, void **);
typedef HRESULT(WINAPI *SaveSurfaceFn)(const wchar_t *, DWORD, void *, const void *, const RECT *);
typedef HRESULT(WINAPI *SwapBackBufferFn)(void *, UINT, DWORD, void **);
static int save_shot(void *dev, void *swap, int t) {
    void *surface = 0;
    wchar_t path[MAX_PATH];
    SaveSurfaceFn save = (SaveSurfaceFn)GetProcAddress(GetModuleHandleW(L"d3dx9_43.dll"), "D3DXSaveSurfaceToFileW");
    HRESULT hr;
    if (!save) return 0;
    if (swap ? ((SwapBackBufferFn)VT(swap, 5))(swap, 0, 0, &surface) < 0 : ((BackBufferFn)VT(dev, 18))(dev, 0, 0, 0, &surface) < 0)
        return 0;
    if (!surface) return 0;
    wcscpy(path, logPath);
    _snwprintf(wcsrchr(path, L'\\') + 1, 64, L"sandbox_shot_%03d.png", t);
    hr = save(path, 3 /* D3DXIFF_PNG */, surface, 0, 0);
    ((Method0)VT(surface, 2))(surface);
    return hr >= 0;
}
static U lastSelected;
static int dumped, testDone;
static void dump(void);
static void frame(void *dev, void *swap) {
    U world = P(image, RVA_WORLD), lp = local_player();
    float time;
    int i;
    if (diag) {
        static DWORD next;
        DWORD now = GetTickCount();
        if (now >= next) {
            U sess = P(image, RVA_SESSION), node, i = 0, humans = 0, h = P(image, RVA_LOCAL);
            next = now + 10000;
            if (sess)
                for (node = P(sess, 0xc8); node && i < 64; node = P(node, 4), i++)
                    if (P(node, 0) && B(P(node, 0), 0xc) == 0) humans++;
            sblog("state: world %08x time %.1f, holder %08x local player %08x kingdom %08x, session %08x state %u list %u humans %u",
                  world, world ? F(world, 0xe8) : -1.0f, h, lp, lp ? P(lp, 0x28) : 0, sess, sess ? P(sess, 0xf0) : 0, i, humans);
        }
    }
    if (bisect == 2) return;
    if (!world || !lp || !P(lp, 0x28) || !single_player()) {
        kingdomCount = 0;
        lastSelected = 0;
        return;
    }
    collect(world);
    time = F(world, 0xe8);
    if (foreground() && (GetAsyncKeyState(VK_MENU) & 0x8000) && !(GetAsyncKeyState(VK_CONTROL) & 0x8000)) {
        for (i = 0; i < 8; i++)
            if (pressed('1' + i) && i < kingdomCount) take_control(kingdoms[i].k, "hotkey");
    } else if (foreground() && (GetAsyncKeyState(VK_CONTROL) & 0x8000) && (GetAsyncKeyState(VK_SHIFT) & 0x8000)) {
        if (pressed('A')) {
            aiOff = !aiOff;
            sblog("computer players %s (skipped %ld, ran %ld thinks so far)", aiOff ? "off" : "on", thinkSkipped, thinkPassed);
        }
        if (pressed('H')) listShown = !listShown;
    } else
        for (i = 0; i < 256; i++) keyDown[i] = 0;
    {
        U owner = selected_owner();
        if (owner && owner != lastSelected && is_player_kingdom(owner)) take_control(owner, "selection");
        lastSelected = owner;
    }
    if (diag && !dumped && time > 5.0f) {
        dumped = 1;
        dump();
    }
    if (testSwitch && !testDone && time > 20.0f) {
        testDone = 1;
        if (testSwitch <= kingdomCount) take_control(kingdoms[testSwitch - 1].k, "test");
        sblog("test: thinks skipped %ld, ran %ld; game time %.1f", thinkSkipped, thinkPassed, time);
    }
    if (listShown && font && kingdomCount && bisect != 1) {
        if (((Method0)VT(dev, 41))(dev) >= 0) {
            draw_list(dev);
            ((Method0)VT(dev, 42))(dev);
        }
    }
    for (i = 0; i < shotCount; i++)
        if (shots[i] > 0 && time >= (float)shots[i]) {
            sblog("shot at game time %.1f: %s", time, save_shot(dev, swap, shots[i]) ? "saved" : "failed");
            shots[i] = 0;
        }
}
static void overlay(void *dev, void *swap, const char *path) {
    if (!presentThread) {
        presentThread = GetCurrentThreadId();
        sblog("present: first frame (%s) on thread %lu, back buffer %ux%u", path, presentThread, backW, backH);
    }
    if (!font && backH) {
        CreateFontFn create = (CreateFontFn)GetProcAddress(GetModuleHandleW(L"d3dx9_43.dll"), "D3DXCreateFontW");
        fontHeight = (int)(backH * 0.0195f);
        if (fontHeight < 14) fontHeight = 14;
        if (!create || create(dev, fontHeight, 0, 700, 1, FALSE, 1, 0, 4, 0, L"Tahoma", &font) < 0) font = 0;
        if (!font) sblog("overlay: D3DXCreateFontW failed");
    }
    frame(dev, swap);
}
/* In-game overlays (Discord's, the Steam overlay) rewrite d3d9's function tables in memory, and the game's renderer
 * creates its device through an IDirect3D9 it looks up by name, not through the import. Every path ends in d3d9's own
 * CreateDevice, Present and Reset code, so those get Windows' hot-patch jump (the 5 spare bytes before the function and
 * its 2-byte "mov edi, edi"). CreateDevice's original address comes from d3d9.dll's file, since the table entry in
 * memory may already point at an overlay. Code that cannot be hot-patched falls back to table entries. */
static U d3d9Base, d3d9Size;
static void *device;
static int in_d3d9(const void *p) { return d3d9Base && (U)p - d3d9Base < d3d9Size; }
/* The value d3d9.dll's file holds for a pointer slot inside its image, relocated to where it is loaded. */
static void *file_slot(void *const *slot) {
    static unsigned char *file;
    static DWORD size;
    U rva = (U)slot - d3d9Base, i, n, sections, base, value;
    if (!in_d3d9(slot)) return 0;
    if (!file) {
        wchar_t path[MAX_PATH];
        HANDLE h;
        DWORD got = 0;
        if (!GetModuleFileNameW((HMODULE)d3d9Base, path, MAX_PATH)) return 0;
        h = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, 0, 0);
        if (h == INVALID_HANDLE_VALUE) return 0;
        size = GetFileSize(h, 0);
        file = size && size < 64 << 20 ? VirtualAlloc(0, size, MEM_COMMIT, PAGE_READWRITE) : 0;
        if (!file || !ReadFile(h, file, size, &got, 0) || got != size) size = 0;
        CloseHandle(h);
        if (!size) return 0;
    }
    if (size < 0x400 || P(file, 0x3c) > size - 0x100) return 0;
    n = P(file, 0x3c);
    sections = *(unsigned short *)(file + n + 6);
    base = P(file, n + 0x34);
    n += 0x18 + *(unsigned short *)(file + n + 0x14);
    for (i = 0; i < sections && n + 40 <= size; i++, n += 40) {
        U va = P(file, n + 12), raw = P(file, n + 16), at = P(file, n + 20);
        if (rva >= va && rva + 4 <= va + raw && at + (rva - va) + 4 <= size) {
            value = P(file, at + (rva - va)) - base + d3d9Base;
            return in_d3d9((void *)value) ? (void *)value : 0;
        }
    }
    return 0;
}
static int hotpatch(void *code, void *hook) {
    unsigned char *p = code;
    DWORD prot, ignored;
    int i;
    if (!in_d3d9(p) || p[0] != 0x8b || p[1] != 0xff) return 0;
    for (i = 1; i <= 5; i++)
        if (p[-i] != 0x90 && p[-i] != 0xcc) return 0;
    if (!VirtualProtect(p - 5, 7, PAGE_EXECUTE_READWRITE, &prot)) return 0;
    p[-5] = 0xe9;
    *(U *)(p - 4) = (U)hook - (U)p;
    *(volatile unsigned short *)p = 0xf9eb; /* jmp short to the jump above */
    VirtualProtect(p - 5, 7, prot, &ignored);
    FlushInstructionCache(GetCurrentProcess(), p - 5, 7);
    return 1;
}

static volatile LONG presentDepth;
static HRESULT present_through(PresentFn next, void *dev, const RECT *a, const RECT *b, HWND w, const void *r) {
    HRESULT hr;
    /* A table-entry hook and a code hook can both sit in one call chain: draw once. */
    if (InterlockedIncrement(&presentDepth) == 1 && dev == device) overlay(dev, 0, "device");
    hr = next(dev, a, b, w, r);
    InterlockedDecrement(&presentDepth);
    return hr;
}
static HRESULT reset_through(ResetFn next, void *dev, void *pp) {
    HRESULT hr;
    if (dev != device) return next(dev, pp);
    if (font) ((Method0)VT(font, 16))(font); /* OnLostDevice */
    hr = next(dev, pp);
    if (hr >= 0 && pp) {
        backW = ((U *)pp)[0];
        backH = ((U *)pp)[1];
        if (font) ((Method0)VT(font, 17))(font); /* OnResetDevice */
    }
    sblog("reset: 0x%08lx, back buffer %ux%u", (unsigned long)hr, backW, backH);
    return hr;
}

/* Up to four distinct Present and Reset functions (d3d9 switches the device between function tables). */
#define CODE_HOOKS 4
static void *presentCode[CODE_HOOKS], *resetCode[CODE_HOOKS];
static PresentFn presentNext[CODE_HOOKS];
static ResetFn resetNext[CODE_HOOKS];
static volatile LONG presentCodes, resetCodes;
#define CODE_HOOK(i)                                                                                                   \
    static HRESULT WINAPI present_code##i(void *d, const RECT *a, const RECT *b, HWND w, const void *r) {              \
        return present_through(presentNext[i], d, a, b, w, r);                                                         \
    }                                                                                                                  \
    static HRESULT WINAPI reset_code##i(void *d, void *pp) { return reset_through(resetNext[i], d, pp); }
CODE_HOOK(0)
CODE_HOOK(1)
CODE_HOOK(2)
CODE_HOOK(3)
static void *const presentHooks[CODE_HOOKS] = {(void *)present_code0, (void *)present_code1, (void *)present_code2,
                                               (void *)present_code3};
static void *const resetHooks[CODE_HOOKS] = {(void *)reset_code0, (void *)reset_code1, (void *)reset_code2,
                                             (void *)reset_code3};
/* 1: hooked now or before, 0: not hot-patchable or no room. */
static int hook_code(void *code, void **done, void *const *hooks, void **next, volatile LONG *count) {
    LONG i, n = *count;
    for (i = 0; i < n; i++)
        if (done[i] == code) return 1;
    if (n >= CODE_HOOKS) return 0;
    next[n] = (unsigned char *)code + 2;
    if (!hotpatch(code, hooks[n])) return 0;
    done[n] = code;
    InterlockedExchange(count, n + 1);
    return 1;
}

/* Fallback for a function table whose Present or Reset code cannot be hot-patched: its entries point at these hooks. */
typedef struct { void **vt; PresentFn present; ResetFn reset; } Table;
static Table tables[8];
static volatile LONG tableCount;
static HRESULT WINAPI present_hook(void *, const RECT *, const RECT *, HWND, const void *);
static HRESULT WINAPI reset_hook(void *, void *);
static Table *table_of(void *dev) {
    void **vt = *(void ***)dev;
    LONG i;
    for (i = 0; i < tableCount; i++)
        if (tables[i].vt == vt) return &tables[i];
    return tableCount ? &tables[0] : 0;
}
static HRESULT WINAPI present_hook(void *dev, const RECT *a, const RECT *b, HWND w, const void *r) {
    return present_through(table_of(dev)->present, dev, a, b, w, r);
}
static HRESULT WINAPI reset_hook(void *dev, void *pp) { return reset_through(table_of(dev)->reset, dev, pp); }

static void hook_table(void *dev) {
    void **vt = *(void ***)dev;
    LONG i, n = tableCount;
    wchar_t name[MAX_PATH];
    for (i = 0; i < n; i++)
        if (tables[i].vt == vt) {
            if (vt[17] != (void *)present_hook) patch_ptr(&vt[17], (void *)present_hook, 0);
            if (vt[16] != (void *)reset_hook) patch_ptr(&vt[16], (void *)reset_hook, 0);
            return;
        }
    {
        static void **seen[16];
        static int seenCount;
        int k, known = 0, present, reset;
        for (k = 0; k < seenCount; k++)
            if (seen[k] == vt) known = 1;
        if (known) return;
        if (seenCount < 16) seen[seenCount++] = vt;
        present = in_d3d9(vt[17]) ? hook_code(vt[17], presentCode, presentHooks, (void **)presentNext, &presentCodes)
                                  : presentCodes > 0; /* another module's hook in front of d3d9's code, hooked earlier */
        reset = in_d3d9(vt[16]) ? hook_code(vt[16], resetCode, resetHooks, (void **)resetNext, &resetCodes) : resetCodes > 0;
        sblog("device: function table %p: Present %p (%ls) %s", vt, vt[17], owner(vt[17], name),
              present && reset ? "code hooked" : "entries hooked");
        if (present && reset) return;
    }
    if (n >= 8) return;
    tables[n].vt = vt; /* recorded before the entries point at the hooks */
    tables[n].present = (PresentFn)vt[17];
    tables[n].reset = (ResetFn)vt[16];
    InterlockedExchange(&tableCount, n + 1);
    if (!patch_ptr(&vt[17], (void *)present_hook, 0) || !patch_ptr(&vt[16], (void *)reset_hook, 0))
        sblog("device: function table %p not hooked", vt);
}
static DWORD WINAPI watch(void *unused) {
    for (;;) {
        Sleep(250);
        if (device) hook_table(device);
    }
    return 0;
}
/* The game's device: its window was created by k2.exe (overlays create devices of their own on hidden windows). */
static void found_device(void *dev, HWND focus, void *pp) {
    HWND w = focus ? focus : (HWND)((U *)pp)[7];
    if ((uintptr_t)GetWindowLongW(w, -6 /* GWL_HINSTANCE */) != image || device) return;
    backW = ((U *)pp)[0];
    backH = ((U *)pp)[1];
    device = dev;
    hook_table(dev);
    CreateThread(0, 0, watch, 0, 0, 0);
}
static CreateDeviceFn createNext;
static HRESULT WINAPI create_device_code(void *d3d, UINT adapter, DWORD type, HWND focus, DWORD flags, void *pp,
                                         void **out) {
    HRESULT hr = createNext(d3d, adapter, type, focus, flags, pp, out);
    sblog("CreateDevice: 0x%08lx device %p, type %lu, window %p", (unsigned long)hr, out ? *out : 0, type, focus);
    if (hr >= 0 && out && *out && pp) found_device(*out, focus, pp);
    return hr;
}
/* Fallback when d3d9's CreateDevice cannot be hot-patched: the import and the shared table entry, as before. */
static HRESULT WINAPI create_device_hook(void *d3d, UINT adapter, DWORD type, HWND focus, DWORD flags, void *pp,
                                         void **out) {
    HRESULT hr = realCreateDevice(d3d, adapter, type, focus, flags, pp, out);
    sblog("CreateDevice (table entry): 0x%08lx device %p", (unsigned long)hr, out ? *out : 0);
    if (hr >= 0 && out && *out && pp) found_device(*out, focus, pp);
    return hr;
}
static void *WINAPI create9_hook(UINT sdk) {
    void *d3d = realCreate9(sdk);
    int ok = 0;
    if (d3d && !realCreateDevice) ok = patch_ptr(&VT(d3d, 16), (void *)create_device_hook, (void **)&realCreateDevice);
    sblog("Direct3DCreate9: %p, function table %p, CreateDevice entry hooked %d", d3d, d3d ? *(void **)d3d : 0, ok);
    return d3d;
}
/* At install: find d3d9's CreateDevice through an IDirect3D9 of this DLL's own, and hot-patch it. */
static int hook_d3d9(void *create) {
    void *d3d, *code;
    wchar_t name[MAX_PATH];
    HMODULE m = GetModuleHandleW(L"d3d9.dll");
    if (!m) return 0;
    d3d9Base = (U)m;
    d3d9Size = P(m, P(m, 0x3c) + 0x50);
    d3d = ((Create9)create)(32 /* D3D_SDK_VERSION */);
    if (!d3d) return 0;
    code = file_slot(&VT(d3d, 16));
    sblog("d3d9: %08x size %08x, CreateDevice entry %p (%ls), in the file %p", d3d9Base, d3d9Size, VT(d3d, 16),
          owner(VT(d3d, 16), name), code);
    ((Method0)VT(d3d, 2))(d3d); /* Release */
    if (!code) return 0;
    createNext = (CreateDeviceFn)((unsigned char *)code + 2);
    return hotpatch(code, (void *)create_device_code);
}

/* ---- diagnostics (K2SB_DIAG=1) ---- */
static void strings_in(const char *what, U obj, U size) {
    U o;
    for (o = 0; o < size; o += 4) {
        U s = P(obj, o), len, i, ok = 1;
        char a[80];
        if (!readable(s - 12, 12) || !(len = P(s, -12)) || len > 60 || !readable(s, len * 2)) continue;
        for (i = 0; i < len; i++) {
            wchar_t c = ((wchar_t *)s)[i];
            if (c < 32 || c > 126) { ok = 0; break; }
            a[i] = (char)c;
        }
        if (!ok) continue;
        a[len] = 0;
        sblog("  %s +0x%03x \"%s\"", what, o, a);
    }
}
static void dump(void) {
    U table = P(image, RVA_RESOURCES), s = P(image, RVA_SESSION), lp = local_player(), node, i;
    int k;
    if (!s || !lp) return;
    sblog("diag: session %08x state %u, local player %08x type %u kingdom #%u; interface kingdom #%u", s, P(s, 0xf0), lp,
         B(lp, 0xc), knum(P(lp, 0x28)), knum(((Get)(image + RVA_LOCAL_KINGDOM))(P(image, RVA_MANAGER), 0)));
    for (node = P(s, 0xc8), i = 0; node && i < 64; node = P(node, 4), i++) {
        U p = P(node, 0);
        if (!p) continue;
        sblog("diag: session player %08x vt %08x type %u kingdom %08x #%u", p, P(p, 0), B(p, 0xc), P(p, 0x28),
             knum(P(p, 0x28)));
    }
    if (table && P(table, 0x38) <= 16)
        for (i = 0; i < P(table, 0x38); i++) {
            U def = P(P(table, 0x34), 4 * i);
            char tag[32];
            sblog("diag: resource %u def %08x kind %u hidden %u pair %08x", i, def, P(def, 0x18), B(def, 0x2c), P(def, 0x30));
            _snprintf(tag, 32, "resource %u", i);
            strings_in(tag, def, 0x80);
        }
    for (k = 0; k < kingdomCount; k++) {
        U kk = kingdoms[k].k, c = P(kk, 0x1f4);
        if (c && !readable(c + 0x18, 12)) c = 0;
        char tag[32];
        sblog("diag: kingdom %08x vt %08x id %08x #%u team %08x colour %08x rgb %.3f %.3f %.3f", kk, P(kk, 0), P(kk, 0x14),
             P(kk, 0x190), P(kk, 0x1f8), c, c ? F(c, 0x18) : -1, c ? F(c, 0x1c) : -1, c ? F(c, 0x20) : -1);
        for (i = 0; i < 6; i++)
            sblog("diag:   r%u prod %.2f cap %.2f cons %.2f stock %.2f", i, F(P(kk, 0x1a8), 4 * i), F(P(kk, 0x1b4), 4 * i),
                 F(P(kk, 0x1c0), 4 * i), F(P(kk, 0x1cc), 4 * i));
        _snprintf(tag, 32, "kingdom #%u", P(kk, 0x190));
        strings_in(tag, kk, 0x600);
    }
    sblog("diag: thinks skipped %ld, ran %ld", thinkSkipped, thinkPassed);
}

/* ---- install, on the game's first SHGetFolderPathW call ---- */
static int expect_push(U rva, U target) {
    return B(image, rva) == 0x68 && P(image, rva + 1) == image + target;
}
static void install(void) {
    static const unsigned char localGetter[] = {0x56, 0x8b, 0xf1, 0xb9};
    static const unsigned char selOwner[] = {0x8b, 0xc8, 0x85, 0xc9, 0x75, 0x01, 0xc3, 0x8b, 0x01, 0xff, 0xa0, 0x08, 0x01, 0x00, 0x00};
    wchar_t value[16];
    HMODULE self;
    void *create;
    DWORD prot, ignored;
    if (InterlockedExchange(&installTried, 1)) return;
    /* The game frees shfolder.dll after asking for the folder: keep this copy loaded. PIN | FROM_ADDRESS */
    GetModuleHandleExW(0x1 | 0x4, (LPCWSTR)(void *)install, &self);
    GetModuleFileNameW(GetModuleHandleW(0), logPath, MAX_PATH);
    wcscpy(wcsrchr(logPath, L'\\') + 1, L"sandbox.log");
    image = (uintptr_t)GetModuleHandleW(0);
    if (GetEnvironmentVariableW(L"K2SB_DIAG", value, 16)) diag = _wtoi(value);
    if (GetEnvironmentVariableW(L"K2SB_TEST_SWITCH", value, 16)) testSwitch = _wtoi(value);
    if (GetEnvironmentVariableW(L"K2SB_BISECT", value, 16)) bisect = _wtoi(value);
    {
        wchar_t list[64], *at = list;
        if (GetEnvironmentVariableW(L"K2SB_SHOTS", list, 64))
            while (*at && shotCount < 8) {
                shots[shotCount++] = _wtoi(at);
                while (*at && *at != L',') at++;
                if (*at) at++;
            }
    }
    create = (void *)GetProcAddress(GetModuleHandleW(L"d3d9.dll"), "Direct3DCreate9");
    sblog("sandbox r1 starting: image %08x, thread %lu", (U)image, GetCurrentThreadId());
    if (!expect_push(RVA_GOAL_PUSH, RVA_GOAL_THINK) || !expect_push(RVA_RAI_PUSH, RVA_RAI_THINK) ||
        memcmp((void *)(image + RVA_LOCAL_KINGDOM), localGetter, sizeof localGetter) ||
        memcmp((void *)(image + RVA_SEL_OWNER + 5), selOwner, sizeof selOwner) || !create ||
        P(image, RVA_D3D_IAT) != (U)create) {
        sblog("not Kohan II 1.3.68 build 24805354 (or already patched): sandbox off, the game runs as shipped");
        return;
    }
    if (!VirtualProtect((void *)(image + RVA_GOAL_PUSH), 0x50, PAGE_EXECUTE_READWRITE, &prot)) return;
    P(image, RVA_GOAL_PUSH + 1) = (U)goal_think;
    P(image, RVA_RAI_PUSH + 1) = (U)rai_think;
    VirtualProtect((void *)(image + RVA_GOAL_PUSH), 0x50, prot, &ignored);
    FlushInstructionCache(GetCurrentProcess(), (void *)(image + RVA_GOAL_PUSH), 0x50);
    realCreate9 = (Create9)create;
    if (hook_d3d9(create))
        sblog("d3d9: CreateDevice code hooked");
    else {
        patch_ptr((void **)(image + RVA_D3D_IAT), (void *)create9_hook, 0);
        sblog("d3d9: CreateDevice code not hot-patchable, using the import and table entries");
    }
    installed = 1;
    sblog("installed: computer players off, control by selection, kingdom list (diag %d, test switch %d)", diag, testSwitch);
}

/* ---- shfolder.dll exports ---- */
typedef HRESULT(WINAPI *FolderPathW)(HWND, int, HANDLE, DWORD, LPWSTR);
typedef HRESULT(WINAPI *FolderPathA)(HWND, int, HANDLE, DWORD, LPSTR);
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
static HRESULT WINAPI folder_path_w(HWND window, int csidl, HANDLE token, DWORD flags, LPWSTR path) {
    wchar_t over[MAX_PATH];
    DWORD n = GetEnvironmentVariableW(L"K2SB_DOCUMENTS", over, MAX_PATH); /* test runs only: own Documents folder */
    FolderPathW system;
    install();
    if (path && n > 0 && n < MAX_PATH && (csidl & 0xff) == 0x0005 /* CSIDL_PERSONAL */) {
        if (csidl & 0x8000) CreateDirectoryW(over, 0);
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
/* The game asks for the plain names; TCC decorates stdcall exports, so the exports are assembler entry points that
 * jump to the stdcall functions (stack and registers untouched). */
void *sb_folder_path_w = (void *)folder_path_w, *sb_folder_path_a = (void *)folder_path_a;
__declspec(dllexport) void SHGetFolderPathW(void);
__declspec(dllexport) void SHGetFolderPathA(void);
__asm__(".globl SHGetFolderPathW\nSHGetFolderPathW:\n jmp *sb_folder_path_w\n"
        ".globl SHGetFolderPathA\nSHGetFolderPathA:\n jmp *sb_folder_path_a\n");
BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID unused) { return TRUE; }
