# Sandbox (single player, Kohan II 1.3.68)

A test sandbox for one player, offered on the Components page for Vanilla and Immortals when the game folder holds the
Steam public-branch `k2.exe` of 1.3.68 (build 24805354, SHA256 `CB646763...AFB9DF7A`).

| Feature | What a player sees |
| --- | --- |
| Computer players off | Computer kingdoms never build, recruit or attack. Units still guard and fight back. Ctrl+Shift+A turns them back on. |
| Control follows selection | Click any unit or building of a player kingdom and that kingdom becomes yours: top bar, heroes, interface skin, build and recruit menus and orders. No hotkeys or manual switch. |
| Second resource row | The other player kingdom's economy shows in the game's own slide-out row under the resource bar (the one that previews a hovered action's cost), with the game's widgets. A hovered build or recruit button still shows its cost there. 1.3.68 port of Paw's `game/ally-economy`; the panel holds one kingdom, so a 2v2 shows one extra row. |
| Shared vision | Always on. The view shows what every player kingdom sees and has explored, the way it shows an ally's sight. Places no player kingdom sees stay fogged; diplomacy and every kingdom's own vision are unchanged. |

It acts only with exactly one human player in the session. With more players, or any other executable, the game runs
as shipped and `sandbox.log` says why.

## How the launcher runs it

The game folder is never changed. When the Sandbox switch is on, Launch:
1. runs the launcher's normal file checks on the game folder;
2. brings the launcher's copy of the game folder up to date (`Sandbox game` next to the launcher; `.pawpatch` and
   `Logs` are left out, and only files the copy placed are ever removed from it);
3. writes the stand-in `shfolder.dll` there, checks the copy's `k2.exe` hash, and starts the copy with
   `SteamAppId=97130` (Steam must be running).

The multiplayer check and fingerprint still describe the game folder, which holds no sandbox files.

## How it works

`sandbox.c` (x86 TCC) is loaded by the game as `shfolder.dll` from its own folder; its `SHGetFolderPathW` and
`SHGetFolderPathA` forward to the system DLL. On the game's first Documents lookup it checks the executable's bytes at
every place it changes, then:

| What | 1.3.68 RVA | Why |
| --- | --- | --- |
| `push GoalEngineThink` in the strategic AI fiber loop | `1E154A` | wrapper clears the AI player's active byte (`+4`) for the call, so the think takes the game's own skip-and-reschedule path |
| `push` of the RAI think | `1E1590` | same |
| `mov eax, [eax+0x234]` in the explored and seen-now checks | `249E05`, `249E2D` | `call` to a stub: for the kingdom you control, on the interface thread, the union of every player kingdom's vision mask (below); otherwise the kingdom's own |
| `mov ecx, [esi+0x234]` in the terrain fog builder | `2D5318` | same, for the fog drawn on the map |
| d3d9's own `CreateDevice`, `Present` and `Reset` code | (d3d9.dll) | Windows' hot-patch jump (the 5 spare bytes before each function and its 2-byte `mov edi, edi`): the game's device is found (test screenshots) |
| EconomicImpact vtable slots `48BBEC` tick, `48BB08` resource widget tick, `48BC2C` / `48BBE4` resets | `BFB73`, `BF8BC`, `BEF93`, `C04F8` | assembler stubs as in Paw's build (flags, registers, FPU/SSE kept); the tick stubs return to the caller, the reset stubs jump into the native function with the caller's stack untouched |
| `call 0x42a446` in the build-menu tooltip | `E8326` | stub: when no buildable variant of a recruit's required structure exists, the tooltip names the structure itself (the game's other caller, `EC596`, already does this); without it Battleborn 0.2.5's Council recruits crashed the game when a Council kingdom's build menu opened |
| (no patch) after a selection switch | `1F45CE` / `1F4544` | the current selection is sent again for the local player with the game's KKC_SelectionSetOrder / KKC_SelectionAddOrder, as its AI does: build and recruit orders act on that simulation-side selection, which the click on another kingdom's town had filled before the switch |
| `call 0x55d4d0` (set the order's player) in the selection sends and actor command sends | `1F4613`, `1F4589`, `2DA42C`, `2DA32B`, `2DA3B1`, `D490A`, `D4883` | stub: while you control another player kingdom, those orders go out as that kingdom's own session player, as its AI sends them; without it the simulation refused your recruit for that kingdom. Pause, speed, saves and chat stay yours |
| application-state setter entry | `13BD34` | closes the row before the world and its kingdoms are destroyed on quit |

Why d3d9's code and not its function tables: the 1.3.68 renderer creates its device through an `IDirect3D9` it looks
up by name (the `d3d9.dll` / `Direct3DCreate9` strings at RVA `5B2484`), not through the import, and in-game overlays
such as Discord's rewrite d3d9's table entries in memory, also after another hook was placed. Every path ends in
d3d9's code. `CreateDevice`'s original address is read from d3d9.dll's file; the game's device is the one whose window
k2.exe created (overlays make devices of their own). Code that is not hot-patchable falls back to the import and table
entries (a watcher re-hooks them every 250 ms, since d3d9 rewrites the `Present` entry after the first frame).

Vision: a kingdom's `+0x234` says whose sight it gets, one bit per kingdom slot (low 16 bits explored, high 16 seen
now): its own and its allies', which is how allies share vision (2v2 teams: `0x00030003` and `0x000c000c`). Map cells
and units carry the same bits, and the checks at `249DF4` / `249E1C` AND them with that mask (units through an actor
virtual). Shared vision changes what those loads return only on the interface thread and only for the kingdom you
control; the simulation runs on its own thread with every kingdom's real mask, so units, AI and diplomacy see nothing
new. Hidden 2v2 runs: with it on the minimap shows all four bases, with it off two; unexplored map stays black.

Control switches write the local session player's kingdom (`[5D7FE4]+0xC`, field `+0x28`): the interface getter and the
order checks both read it. Names and colours are read every 2 s, never per frame: per-frame `VirtualQuery` calls in
`Present` kept the simulation thread from starting.

Build: `build.ps1 -NativeCompiler <tcc.exe>` rebuilds `src/PawsPatchLauncher/Assets/Sandbox/shfolder.dll`.
