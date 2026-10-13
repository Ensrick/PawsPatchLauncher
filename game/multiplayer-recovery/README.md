# Multiplayer recovery, revision 1

An optional native module for the Vanilla/Immortals pure beta runtime on Kohan II 1.3.72 (Steam build 25068126).
The helper installs it after the lobby compatibility module when the component `pure-multiplayer-recovery` is
enabled. It has three features, all made from the game's own commands and orders:

| Feature | What a player sees |
| --- | --- |
| Save copies | When the host saves a multiplayer match (by hand or by autosave), every other player's game writes the same moment as `<name> (copy).RSG`. If the host's PC is gone, anyone can host the match again. |
| Multiplayer autosave | The host's game saves the running match every 5 minutes into `MP Autosave A` and `MP Autosave B` in turn. The game's own autosave is single-player only. |
| Drop recovery | When a player drops out of a running match (crash, lost connection), the host's game saves `MP Resume <date> <time>` and pauses. It then takes everyone back to the lobby, still connected, with that save selected. The dropped player joins again, the host launches, and the match continues. A deliberate quit is left alone. |

Drop recovery also votes to drop a player whose connection stays down for 20 s while the game waits in its
connection dialog. Over Steam a dropped peer never causes the automatic removal that a LAN socket error triggers,
so without a vote the match waits forever (see [the 1.3.68 research notes](../../docs/reconnect-research-1368.md)).

## How it works

`MultiplayerRecovery.cs` embeds `paws_multiplayer_recovery.dll` and loads it into the fresh, verified game with the
lobby module's handshake:
1. the system loader identity is checked;
2. a worker thread reports ready;
3. the whole process is paused;
4. only the worker runs `PawInstall`;
5. the result and the features turned on are read back before the game resumes.

An install that fails is logged as `MULTIPLAYER_RECOVERY_OFF` and the game runs unchanged. An aborted install
releases the waiting worker with a configuration the module rejects before any change.

The module (`multiplayer_recovery.c`, x86 TCC) checks 37 byte signatures (relocated operands are wildcards). A
feature whose code differs stays off and the log says which signature failed. It then changes:

| What | 1.3.72 RVA | Why |
| --- | --- | --- |
| `call BeginSoloTask` in `KKC_Session::GameSave`, client branch | `162B94` | after it, write the copy (`KKC_ResourceManagerTaskSaveGame`, queue, `TGC_ResourceManagerTask::Block`) while the game is paused for the host's save |
| `call CreateSaveGameTask`, host branch | `162C3A` | take a reference on the save task it stored in the session (`+134`) and hand it to the tick, which follows its status (`+14`: 2 finished, 3 failed), logs the save's lobby checksum, and lets the drop-recovery lobby step wait for `MP Resume` without looking at files |
| `call TGC_AdminNode::PlayerLeave` in the leave decree | `15708C` | note a player leaving |
| `call SerializeClientDisconnectRequest` in the packet reader | `1503EA` | note a player asking to leave, so the leave that follows within 3 s is a quit, not a drop |
| the pointer the main loop's message pump calls `PeekMessage` through (`call [ptr]` at `2F5391`) | `5D4314` | a tick on the game's main thread, every frame, at most every 50 ms. Replaced only once the game has resolved it to `user32`'s `PeekMessageW`/`A` (before first use it points at a resolver that would overwrite it). `timeGetTime` is not usable: 1.3.72 calls it only in a start-up memory benchmark. |

Each redirected call checks its current target first. In 1.3.72 the simulation runs on its own thread (`SIM`), and
the save order, the leave decree and therefore these hooks run there. The client save hook writes the copy right
there while the simulation is frozen for the host's save; on the rig that took 31 ms. The other hooks only record
events. Everything else runs from the main-thread tick:

- **Script commands:** `GameSave`, `GamePause 1`, `CSDKickLaggingPlayers 0` and `ScoringExit`, run through the
  console the way a `+command` on the command line runs them.
- **End Mission:** the host's own End Mission at RVA `C3EFB` takes everyone to the lobby still connected: state 1,
  map type 4 "use existing", then Launch.
- **Selecting the save:** `KKC_HostSetLoadSavedGameOrder`, ctor `121BD0`, issue `122822`. The order is sent 1.5 s
  after arriving in the lobby, because an order sent with the switch is refused "due to versioning". It is
  confirmed and resent at most 5 times.

Field offsets read: session `+68` (file), `+F0` (state), `+100` (networked), `+128` (countdown); admin node
`+2E4` (server), `+2F4` (backup mode, the flag `SetBackupMode` writes), `+300` (host kind: 1 after taking over
from a lost host).

Safety rules:
- **Save copies:** checked against the save task's own source walk first, so a copy is skipped where the game's
  save code would fault (a lobby that switched from a saved game to a random map, seen on 1.3.68).
- **Drop recovery:** acts only on the original host of a running networked multiplayer match, outside backup
  mode and the launch countdown. After a host takeover it saves and pauses but stays in the match.
- **Names:** each drop gets a new `MP Resume` name, so no player is asked "A different version of this map exists -
  overwrite it?".
- **Log:** `<game>\paws_multiplayer_recovery_1372.log`, with save names relative to the game's data folder and no
  user paths.

## Build

```powershell
game/multiplayer-recovery/build.ps1 -OutputDirectory <native> -NativeCompiler <x86-tcc> [-MappedImage <snapshot>]
```

Run it before `game/pure-fixes/build_beta.py --native <native>`, which embeds the DLL. It builds the DLL and
then runs three checks:

- `MultiplayerRecoveryTests.exe` (`-DPAW_TEST`), 82 offline checks:
  - plain-memory state decisions: host of a running match, End Mission allowed, connection down;
  - copy names, the lobby checksum and the signature table format;
  - every hook called the way the game calls it (thiscall: `ECX` + stack arguments popped by the callee). Each
    hook must pass its arguments to the original, keep the stack and saved registers, and record its event.
- with `-MappedImage` (the hash-pinned 1.3.72 snapshot), 125 checks: all 37 signatures and all 6 call sites.
- `MultiplayerRecoveryInstallerTests.exe`, 13 checks: the configuration block's layout and limits.

## Validation

- **Re-mapping:**
  - every address was re-mapped from the 1.3.68 module by its signature, with call offsets ignored;
  - ambiguous matches were resolved through call sites, RTTI (`KKC_HostSetLoadSavedGameOrder` vtable) and
    identical function layouts;
  - `End Mission`, `SetBackupMode`, the `+300` uses and the save task's globals were compared instruction by
    instruction with 1.3.68.
- **Test rig** (`tools/MultiplayerRig`): two copies of 1.3.72 in a LAN match on one PC, with the module installed
  by `RigInject.exe` through this installer code. Results are in the pull request.
- **Not yet done:** real separate PCs over Steam. The two-copy rig runs over LAN loopback, where a socket error
  removes a dropped player automatically, so the vote path is only reachable over Steam.

## Known limits

- **Host crash:** handled only partly. The player who takes over saves and pauses but keeps the match. Bringing the
  lost host back needs that player's lobby to be found, which is untested.
- **Name of the copies:** they keep `(copy)` because they are not byte-identical to the host's save: on the rig a
  client's copy was about 24 bytes smaller, apparently per-PC data. The host's and the copies' lobby checksums are
  logged. Loading a copy as host is a separate test (see the pull request).
- **Players without the module:** a player without it is not affected beyond the pauses for the host's saves, which
  every multiplayer save already causes.
