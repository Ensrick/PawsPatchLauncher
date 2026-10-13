# Multiplayer saves, revision 1

An optional native module for the Vanilla/Immortals pure beta runtime on Kohan II 1.3.72 (Steam build 25068126).
The helper installs it after the lobby compatibility module when the component `pure-multiplayer-saves` is enabled.

| Feature | What a player sees |
| --- | --- |
| Save copies | When the host saves a multiplayer match (by hand or by autosave), every other player's game writes the same moment as `<name> (copy).RSG`. If the host's PC is gone, anyone can host the match again from it. |
| Multiplayer autosave | The host's game saves the running match every 5 minutes into `MP Autosave A` and `MP Autosave B` in turn. The game's own autosave is single-player only. |

Disconnects, the return to the lobby and drop votes belong to the lobby module's match recovery. This module never
acts on them, and it changes no code that match recovery uses.

## How it works

`MultiplayerSaves.cs` embeds `paws_multiplayer_saves.dll` and loads it into the fresh, verified game with the lobby
module's handshake:
1. the system loader identity is checked;
2. a worker thread reports ready;
3. the whole process is paused;
4. only the worker runs `PawInstall`;
5. the result and the features turned on are read back before the game resumes.

An install that fails is logged as `MULTIPLAYER_SAVES_OFF` and the game runs unchanged. An aborted install releases
the waiting worker with a configuration the module rejects before any change.

The module (`multiplayer_saves.c`, x86 TCC) checks 25 byte signatures (relocated operands are wildcards). A feature
whose code differs stays off, and the log names the signature. It then changes:

| What | 1.3.72 RVA | Why |
| --- | --- | --- |
| `call BeginSoloTask` in `KKC_Session::GameSave`, client branch | `162B94` | after it, write the copy (`KKC_ResourceManagerTaskSaveGame`, queue, `TGC_ResourceManagerTask::Block`) while the game is paused for the host's save |
| the pointer the main loop's message pump calls `PeekMessage` through (`call [ptr]` at `2F5391`) | `5D4314` | a tick on the game's main thread, every frame, at most every 50 ms, for autosave. Replaced only once the game has resolved it to `user32`'s `PeekMessageW`/`A` (before first use it points at a resolver that would overwrite it). |

The save order runs on the simulation thread (`SIM`), so the copy is written there, while the simulation is frozen
for the host's save; on the rig that took 16-32 ms. Autosave runs the game's own `GameSave` script command from the
main-thread tick.

## Saves only where the game can save

Every save the module makes first runs the save task's own source walk, read-only
(`KKC_ResourceManagerTaskSaveGame::SaveGame`, RVA `1C1A15`). It skips the save where the game's save code would stop
at an assertion:
- a lobby that switched from a saved game to a random map (seen on 1.3.68);
- a player who received the saved match over the network (1.3.72): that save's header is not loaded on that PC. The
  save task then asserts `source_res->IsHeaderLoaded()` on the `ASYNC` thread while holding a lock the main thread
  waits for, and after 60 s the game ends with "timeout waiting for mutex".

So in a match loaded from a save (including a resumed one), only the host, who loaded the file from its own disk,
saves. Other players write no copies there, and a player who takes over from a lost host does not autosave. The
module logs why.

## Saves never overlap

Autosave queues `GameSave` only while nothing save-related is busy. A busy save defers the autosave to a later tick;
it does not skip it.
- **The game:** no save task is pending in the session (`+134`, which the session clears when the task has ended).
- **This module:** no own save is in flight. It counts from the `GameSave` command until its task has appeared in that
  slot and left it again: at most 30 s for the task to appear, 2 minutes in all.
- **Other modules:** no other loaded module's `PawSaveBusy` is non-zero.

`PawSaveBusy` is the shared contract between Paw modules in one game. A module that queues or writes a save exports
`volatile LONG PawSaveBusy`, non-zero from just before it queues the save until that save has ended or it gave up.
This module sets it for each autosave and each copy. It reads the other modules' exports at most once a second. The
helpers load Paw modules with `LoadLibraryW` and never free them, so the pointers stay valid.

The log is `<game>\paws_multiplayer_saves_1372.log`, with save names relative to the game's data folder and no user
paths.

## Build

```powershell
game/multiplayer-saves/build.ps1 -OutputDirectory <native> -NativeCompiler <x86-tcc> [-MappedImage <snapshot>]
```

Run it before `game/pure-fixes/build_beta.py --native <native>`, which embeds the DLL. It builds the DLL and then runs
three checks:

- `MultiplayerSavesTests.exe` (`-DPAW_TEST`), 67 offline checks:
  - autosave decisions, with a command-counting stand-in for the script call:
    - idle: one save;
    - a native save pending, another module busy, or the module's own save queued: deferred, then run once idle;
    - a save that never starts: given up after 30 s;
    - a match this PC cannot save;
  - the source walk on built resource chains;
  - copy names, the lobby checksum and the signature table format;
  - the client save hook called the way the game calls it (thiscall: `ECX` plus stack arguments popped by the
    callee): arguments passed on, stack and saved registers kept.
  Removing any one of the three deferral conditions makes its check fail.
- with `-MappedImage` (the hash-pinned 1.3.72 snapshot), 93 checks: all 25 signatures and the call site.
- `MultiplayerSavesInstallerTests.exe`, 11 checks: the configuration block's layout and limits.

## Known limits

- **Copies are not byte-identical** to the host's save: on the rig a client's copy was 24-91 bytes smaller, apparently
  per-PC data, hence `(copy)`. A copy loads as a hosted match: on the rig the host hosted a client's copy and the
  client joined it.
- **Composition with the full helper:** the module reads the running game after the modules before it have
  installed. A signature over code another module changed turns the dependent feature off with a log line; nothing is
  bypassed.
- **Players without the module** are not affected beyond the pauses for the host's saves, which every multiplayer
  save already causes.
