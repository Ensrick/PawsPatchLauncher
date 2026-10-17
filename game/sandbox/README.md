# Sandbox (single player, Kohan II 1.3.68)

A test sandbox for one player, offered on the Components page for Vanilla and Immortals when the game folder holds the
Steam public-branch `k2.exe` of 1.3.68 (build 24805354, SHA256 `CB646763...AFB9DF7A`).

| Feature | What a player sees |
| --- | --- |
| Computer players off | Computer kingdoms never build, recruit or attack. Units still guard and fight back. Ctrl+Shift+A turns them back on. |
| Control any kingdom | Click any unit or building of a player kingdom and that kingdom becomes yours: top bar, heroes, interface skin and orders. Alt+1..8 does the same by list position. |
| Kingdom list | Top left: colour, team, name and economy (gold, stone, wood, iron, mana, unit limit) of every player kingdom in slot order; the one you control is highlighted. Ctrl+Shift+H hides it. |

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
| d3d9's own `CreateDevice`, `Present` and `Reset` code | (d3d9.dll) | Windows' hot-patch jump (the 5 spare bytes before each function and its 2-byte `mov edi, edi`): the game's device is found and the kingdom list is drawn (D3DX font) |

Why d3d9's code and not its function tables: the 1.3.68 renderer creates its device through an `IDirect3D9` it looks
up by name (the `d3d9.dll` / `Direct3DCreate9` strings at RVA `5B2484`), not through the import, and in-game overlays
such as Discord's rewrite d3d9's table entries in memory, also after another hook was placed. Every path ends in
d3d9's code. `CreateDevice`'s original address is read from d3d9.dll's file; the game's device is the one whose window
k2.exe created (overlays make devices of their own). Code that is not hot-patchable falls back to the import and table
entries (a watcher re-hooks them every 250 ms, since d3d9 rewrites the `Present` entry after the first frame).

Control switches write the local session player's kingdom (`[5D7FE4]+0xC`, field `+0x28`): the interface getter and the
order checks both read it. Names and colours are read every 2 s, never per frame: per-frame `VirtualQuery` calls in
`Present` kept the simulation thread from starting.

Build: `build.ps1 -NativeCompiler <tcc.exe>` rebuilds `src/PawsPatchLauncher/Assets/Sandbox/shfolder.dll`.
