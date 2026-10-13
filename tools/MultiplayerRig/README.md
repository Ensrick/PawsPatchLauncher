# Multiplayer test rig (Kohan II 1.3.72)

A developer tool: two copies of the game play a LAN match against each other on one PC, driven by the game's own
script commands, with the multiplayer saves module (`game/multiplayer-saves`) installed in each copy by the same
installer code the pure helper uses. It tests multiplayer changes without a second player or PC.

It starts the real game. Never run it while someone is playing; it refuses to start when `k2.exe` is running.
Steam must run (the game's copy protection). The copies play over LAN loopback, not Steam, so the rig shows what
two peers do, not network conditions: a dropped peer there causes a socket error that removes it at once, whereas
over Steam the game waits in its connection dialog for a vote.

```powershell
tools/MultiplayerRig/build.ps1 -OutputDirectory .local/rig -NativeCompiler <x86-tcc>
tools/MultiplayerRig/Run-Rig.ps1 -RigBuild .local/rig -Scenario SaveCopy   # also Autosave, LoadCopy, HostLeaves
```

The exit code is the number of failed checks. The output lists both modules' logs and the checks. `Run-Rig.ps1`'s
header describes each scenario. A peer counts as having survived only when its game log was closed normally (renamed
`-ok`, not `-crash`) and holds no failed assertion, desync, unhandled exception or lock timeout.

Two more scripts use the same launcher (`RigLauncher.ps1`) and stand-in, one copy at a time:

| Script | What it reports |
| --- | --- |
| `Test-Startup.ps1 -RigBuild .local/rig` | Starts the game as installed, waits for the main menu and quits: every `ERROR` line, failed assertion and warning from start-up. For checking an applied package without playing. |
| `Probe-Precedence.ps1 -RigBuild .local/rig -RwdTool <RwdTool.exe>` | Which copy of a data file the game uses when several depots hold it (see below). Needs `tools/RwdTool` (pull request #1). |

`Probe-Precedence.ps1` packs two one-file archives, mounts them with `addmod` from a startup script, and starts the
game with `+Exec pawsprobe/order.txt`; each candidate copy of that script echoes its own source and quits. Result on
1.3.72:

| Depots holding `pawsprobe/order.txt` | The game used |
| --- | --- |
| `addmod A`, then `addmod B` | archive B |
| `addmod B`, then `addmod A` | archive A |
| both archives and `<game>\data` | the archive mounted last |
| both archives, `<game>\data` and `Documents\Kohan2\data` | `Documents\Kohan2\data` |
| only `<game>\data` (control) | `<game>\data` |

So the archive mounted last wins over earlier archives and over the game's `data` folder, and the player's
`Documents\Kohan2\data` wins over all of them. The probe ran twice with the same result.

## How it works

| Step | How |
| --- | --- |
| Start a copy with its script | `k2.exe +Exec PawsRig/host.txt` (`+command` on the command line runs a script command), `SteamAppId` in its environment, shown without activation and moved past the right edge of the desktop (not minimized: a minimized windowed game gets a 0x0 client area and its renderer fails). |
| Keep both running in the background | `RunWhenInactive = true` in each copy's settings; otherwise a copy that is not in front nearly stops. |
| Own Documents folder per copy | The stand-in `shfolder.dll` (`rig_shfolder.c`): the game finds its user folder with `LoadLibrary("shfolder.dll")` + `SHGetFolderPathW`, Windows takes that DLL from the game folder first, and the stand-in answers with `K2_RIG_DOCUMENTS`. The game frees the DLL after the first call and asks again later, so the stand-in pins itself; without that, a later call reached the system DLL and the game's `%USERDATA%` string turned back to the real Documents folder while its saves still went to the copy's own. |
| Host a LAN game | `RequestMultiplayer`, `RequestLobbyLAN`, `LobbySetLANNickname`, `InternetGameCreate <name> <port> <players>` (a fourth argument is the password), `SelectLaunchType RANDOM_MAP`, `StagingLaunch`. Readiness comes from Windows (the host listens on UDP 5860), because the game buffers its log. |
| Join by address | `LobbyJoinDirect`, `JoinDirectJoin`. No script command fills an editbox, so with `K2_RIG_JOIN=<ip>:<port>` the stand-in answers the Direct IP dialog's two editbox reads (1.3.72 RVAs `EF184`, `EF1A6`, calls of the widget text getter `29FC02`). |
| Install the module | `RigInject.exe <pid> <rig folder> <autosave minutes>`: `MultiplayerRecovery.Prepare` + `Install`, with the module and its log in the copy's own rig folder. |
| Quit | `ReallyQuit` (no confirmation). |

`WaitState <state>` before the main menu crashes the game, so the scripts start with a plain `Wait 45`.

## Keeping the player's game untouched

| Risk | Prevention |
| --- | --- |
| A setting given to a copy ends up in the player's settings, replays or saves | Each copy has its own Documents folder under `%LOCALAPPDATA%\PawsMultiplayerRig`, holding a copy of the player's game data (hand-installed mods included, so both copies load the same data) and none of the player's own files. |
| A test window holds the mouse or plays sound | Each copy's own settings: windowed 800x600, `MouseClipCursor = false`, muted. |
| The player's newest match logs are deleted (the game keeps about ten) | `<game>\Logs` is copied to `%LOCALAPPDATA%\PawsMultiplayerRigLogBackup` before each run. |
| A test overwrites or removes a file it did not create | Every path a script creates in the game folder (`shfolder.dll`, `PawsRig\host.txt` and `client.txt`, the probe's archives, startup script and data file) must not exist before; a leftover from an interrupted run included, so check it and remove it by hand. Afterwards the script removes only those files, file by file, once no copy runs, and a folder only if it created it and it is empty. |
| A copy outlives the run | Each copy is recorded the moment it starts (process id, creation time, executable). After a normal run the rig waits up to 5 minutes for them to close, after a failed setup not at all, and then stops only processes that still match that record. `-TestSetupFailure` checks this: the run fails right after the host copy starts. |
| A save given to LoadCopy is lost | `-LoadSave` is read before the rig empties its copies' Save folders, so a copy from an earlier run (which lies in one of them) can be passed. |
