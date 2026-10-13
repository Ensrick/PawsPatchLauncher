# Multiplayer saves, revision 2

A native module built into the Vanilla/Immortals recovery beta on Kohan II 1.3.72 (Steam build 25068126).
The beta helper installs it after the lobby compatibility/recovery module. It is selected at compile time with
`PAW_PURE_MULTIPLAYER_SAVES`; stable and Arcane Wars builds do not include it. No separate launcher component is required.

| Feature | What a player sees |
| --- | --- |
| Save copies | When the host saves a multiplayer match (by hand or by autosave), every other player's game writes the same moment as `<name> (copy).RSG`. If the host's PC is gone, anyone can host the match again from it. |
| Multiplayer autosave | The host's game saves the running match every 5 minutes into `MP Autosave A` and `MP Autosave B` in turn. The game's own autosave is single-player only. |

Disconnects, the return to the lobby and drop votes belong to the lobby module's match recovery. This module never
acts on them, and it changes no code that match recovery uses.

## How it works

`MultiplayerSaves.cs` embeds `paws_multiplayer_saves.dll` and loads it into the fresh, verified game with the lobby
module's handshake:
1. the system loader identity is checked; the helper waits, without pausing the game, for its delayed message-pump
   import to resolve to the matching `user32` export (a window can exist before that happens);
2. a worker thread reports ready;
3. the whole process is paused;
4. only the worker runs `PawInstall`;
5. the result and the features turned on are read back before the game resumes.

An install that fails is logged as `MULTIPLAYER_SAVES_OFF` and the game runs unchanged. An aborted install releases
the waiting worker with a configuration the module rejects before any change. Native messages share
`Logs/paws_recovery.log`, prefixed with `multiplayer-saves:`; the current launcher already collects that file in diagnostics.

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

## Autosave waits for other saves

Autosave queues `GameSave` only on the game's main thread, and only while nothing save-related is busy. A busy save
defers the autosave to a later tick; it does not skip it. Immediately before queueing, it checks:
- **The game:** the session's save slot (`+134`) is empty. Any task there counts, including a finished one the session
  has not detached yet.
- **This module:** no own save is in flight. It counts from the `GameSave` command until its task has appeared in that
  slot and left it again: at most 30 s for the task to appear, 2 minutes in all.
- **Other modules:** no other loaded module's `PawSaveBusy` is non-zero. A module list that cannot be read, or a
  malformed export, also defers.

`PawSaveBusy` is the shared contract between Paw modules in one game:
- **The flag:** each module that schedules game saves exports an aligned `volatile LONG PawSaveBusy`, initially 0, and
  writes only its own flag, with `InterlockedExchange`. The flag is 1 from just before the module queues a save until
  that work has ended or it gave up.
- **Main thread only:** saves are scheduled only there. The flags coordinate the modules; they are not a lock between
  threads.
- **Who writes it here:** only autosave. Save copies are written on clients inside the host's save and schedule
  nothing.
- **Reading other modules:** this module lists the loaded modules each time. It holds each one with a reference while
  looking up and reading its export, and caches nothing. A valid export is a 4-byte aligned, committed, writable
  location inside the exporting module's image.

Limits:
- **Cooperating producers only.** The flags coordinate the automatic producers that follow this contract (this
  module's autosave and the lobby module's match recovery). They are not an engine-wide lock. A manual save a player
  starts is seen only once its task is in the session's slot; one queued in the moment before that is not.
- **A timeout does not cancel.** When the module gives up on its own save (30 s without a task, 2 minutes in all), it
  clears its flag, but a `GameSave` command already dispatched is not withdrawn. How a late command behaves then has
  not been tested in a real game.

The log is `<game>\Logs\paws_recovery.log`, with save names relative to the game's data folder and no user
paths.

The PMS2 installer configuration supplies the same save-data directory as recovery, including the private-profile
test fixture. A 16 ms save can begin and end between the 50 ms task polls. When no native task is visible, revision 2
can acknowledge a changed, nonempty output that opens without sharing writes. It records the file identity, size and
write time before dispatch: an unchanged older save, an inaccessible baseline, an empty output or an open writer cannot
acknowledge completion. This fallback releases the busy flag after a fast write; it is not validation of save contents.
The live regression loads a client copy as a new hosted match to check loadability separately.

## Build

```powershell
game/multiplayer-saves/build.ps1 -OutputDirectory <native> -NativeCompiler <x86-tcc> [-MappedImage <snapshot>]
```

Pass that DLL to `game/pure-fixes/build_beta.py --saves-native <native>/paws_multiplayer_saves.dll`
alongside `--recovery-native <recovery>/paws_recovery_integrated.dll`. The native build runs
these checks before packaging:

- `MultiplayerSavesTests.exe` (`-DPAW_TEST`), 79 offline checks:
  - autosave decisions, with a command-counting stand-in for the script call:
    - idle: one save;
    - a native save pending, another module busy, the other modules unreadable, or the module's own save queued:
      deferred, then run once idle;
    - a save that never starts: given up after 30 s;
    - a match this PC cannot save;
  - the shared flag: the built DLL exports `PawSaveBusy` (initially 0, a valid flag), and setting it there makes the
    module list read busy; the test's own flag is not counted, and misaligned, code or foreign addresses are not
    valid flags;
  - the source walk on built resource chains;
  - copy names, the lobby checksum and the signature table format;
  - the client save hook called the way the game calls it (thiscall: `ECX` plus stack arguments popped by the
    callee): arguments passed on, stack and saved registers kept.
  Removing any one of the deferral conditions, or deferring only on a busy flag and not on an unreadable list, makes
  its check fail.
- with `-MappedImage` (the hash-pinned 1.3.72 snapshot), 105 checks: all 25 signatures and the call site.
- `FastSaveTests.exe`, 25 checks with real files and sharing locks: a closed output can confirm a
  save missed between polls; unchanged, empty, open-for-writing and unknown-baseline files cannot.
- `MultiplayerSavesInstallerTests.exe`, 14 checks: the PMS2 configuration block's layout and limits.

`game/reconnect-test/installed_saves_pair.py` drives two private LAN copies of a fixture installed by
`PatchMatrixRunner`. It checks the packaged helper hashes, uses ordinary native script commands through the
QA-only dispatcher, and observes session state without changing it. It verifies manual copies, both five-minute
autosave slots, client-loss recovery, host loss after a received-save reload, and hosting a client copy. `--slots 0`
skips the timed autosave segment for an independent recovery-only rerun; it does not claim autosave coverage.
The required artifacts are `InstalledHarness.exe`, `rig_input.dll`, and the private-folder `native/shfolder.dll`.
The marker, distinct Documents directories, and process-path/creation-time checks keep the user's game separate.
The rig does not record the screen or use a virtual machine. A socket closure may reach recovery directly;
the native disconnect vote is issued only if the game remains in backup mode.

## Known limits

- **Copies are not byte-identical** to the host's save and may be larger or smaller, apparently due to
  per-PC data, hence `(copy)`. A copy loads as a hosted match: on the upstream rig the host hosted a client's copy and the
  client joined it.
- **Composition with the full helper:** the module reads the running game after the modules before it have
  installed. A signature over code another module changed turns the dependent feature off with a log line; nothing is
  bypassed.
- **Players without the module** are not affected beyond the pauses for the host's saves, which every multiplayer
  save already causes.
