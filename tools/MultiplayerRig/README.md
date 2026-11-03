# Multiplayer regression rig (developer tool)

Adapted from Ensrick's PR #12, head `c94cd8f85f4a4a7520272276aa51f79990030f0c`.
Copyright and MIT license: [LICENSE.Ensrick](LICENSE.Ensrick).
Local integration adds an isolated stock fixture, per-run retained evidence,
strict process ownership and resource/path guards. Not included in launcher or
patch packages and never started automatically or by CI.

## Start on this PC

Run `D:\PawsMultiplayerRig\Start multiplayer tests.cmd`, then select a scenario.
Steam must be running. Close an ordinary game first; the rig refuses to interrupt
it. Two windowed, muted game processes run off screen. There is no VM recording.
Use Ctrl+C to interrupt: Windows closes the test process job and stops its children.
An interrupted run can leave temporary fixture files; inspect those before retrying.

Results and saves are retained under `D:\PawsMultiplayerRig\runs\<timestamp-scenario>`.
`result.json` contains checks; `game-logs`, `Host/Logs` and `Client/Logs` contain
supporting evidence. Saves are in each peer's `Documents/Kohan2/data/Save`.
Old runs are never emptied or reused. `run.json` also records incomplete setup.

## Prepare another workstation

From the repository, with an x86 TCC compiler and Steam Kohan II 1.3.72:

```powershell
./tools/MultiplayerRig/Prepare-Rig.ps1 -Stock 'D:\SteamLibrary\steamapps\common\Kohan II' -OutputDirectory 'D:\PawsMultiplayerRig' -NativeCompiler '<x86-tcc.exe>'
./tools/MultiplayerRig/Start-Tests.ps1 -Configuration 'D:\PawsMultiplayerRig\rig.json'
```

Optional `-MappedImage` validates native instruction sites against the pinned
1.3.72 analysis snapshot. `Prepare-Rig` accepts a fresh destination only. It copies
an explicit stock-runtime list and pinned loose/skin files; it excludes installed
helper executables, graphics proxies, loose mods and user Documents. Compilation
runs the production multiplayer-save module's native and installer tests.

## Scenarios

| Name | Checks | Approximate duration |
| --- | --- | --- |
| SaveCopy | Host save, nonempty client copy, copy log, both peers exit normally | 5 min |
| Autosave | Two alternating autosaves and client copies; client does not autosave | 7 min |
| LoadCopy | Receive a supplied client copy, load it, original host exits, client takes over | 10 min |
| HostLeaves | Client takes over after host leaves the LAN lobby; reports assertions/crashes | 5 min |

Pass `-Scenario SaveCopy` for noninteractive use. `LoadCopy` additionally accepts
`-LoadSave '<full path to client .RSG>'`; it reads but does not modify the source.
The low-level `Run-Rig.ps1` also accepts port/timing options with bounded values.
Save names cannot contain script commands or path separators.

Exit code 0 means the scenario's checks passed. A failed assertion, unhandled
exception, desync, missing normal exit, timeout or missing expected save is a
failure, even if a process remained alive. A setup error is also nonzero.
HostLeaves intentionally exercises a known engine problem: a reported failure
is useful evidence, not a reason to silently skip that assertion.

## Isolation and ownership

- Only a fixture created by Prepare-Rig is accepted; the installed game is read
  during preparation and never modified by a scenario.
- Each run uses fresh host/client profiles. No personal settings or saves are
  copied, and previous results cannot produce a false positive.
- A fixture lock prevents concurrent runs. At least 4 GiB free RAM and 6 GiB free
  commit are required. Only one pair runs at a time.
- Processes start suspended, enter a kill-on-close Windows Job Object, and then
  resume. Retained process handles prevent cleanup of an unrelated reused PID.
  Injection additionally verifies executable path, hash and creation time.
- Generated scripts/DLL are refused if already present; cleanup removes only
  files created by the current run. Nested filesystem redirects are rejected.
- Private Documents redirection handles both ANSI and Unicode calls and fails
  closed for an absent/invalid private folder. Other shell-folder calls use Windows.

`Test-Safety.ps1 -OutputDirectory <fresh directory>` tests refusal rules, locking,
owned-process cleanup and child termination when its owner is killed, with
lightweight sleeping programs rather than game instances. `Start-Tests.ps1
-TestSetupFailure -Scenario SaveCopy` deliberately fails after starting the host;
a nonzero result plus no remaining game/script/DLL proves cleanup worked.

## What this does and does not verify

### Task-status initialization candidate

`Build-TaskStatusCandidate.ps1 -BaselineBuild <verified rig build> -OutputDirectory <new folder>`
reuses the exact baseline save DLL and private-folder DLL. It compiles the real
`TaskStatusPatch.cs` with the identity-checked process-memory implementation used
by the Pure helper. Pass this new build to `Run-Rig.ps1 -RigBuild ...`.
Its `candidate.json`, per-peer `Logs/task-status.log` and two mandatory report
checks distinguish this experiment from the saves-only baseline. The patch is
installed while the fresh game is paused, before the lobby script starts.
No native enum-reader assertion is bypassed. This still is not a whole-patch run.

`HostLeavesLaunch` extends the lobby-takeover check: the surviving client starts
a new match and writes `After takeover.RSG`. This checks the transition from the
initialized idle status into normal world-loading callbacks, rather than only
survival in the lobby. It does not test direct joining of an already running match.

The [2026-10-22 regression](../../docs/verification-multiplayer-rig-2026-10-22.md)
passed dedicated Autosave and LoadCopy runs, including the received-save guard
after host takeover. LoadCopy now requires these guard paths to be exercised.
The HostLeaves series reproduced the network enum assertion on its second run;
the report records the actual task-status request, decoded value and stack.

This initial integration uses stock Vanilla data and injects the current production
`multiplayer-saves` module through its normal installer. It does not inject the
entire Paw's Patch, the recovery-to-lobby module or the experimental enum assertion
skip. Therefore success is not full beta acceptance or proof of direct rejoining
into a running simulation. Immortals/Arcane fixtures and whole-patch recovery
scenarios can be added explicitly later.

Both peers use loopback LAN on one PC. Steam disconnection votes, real latency,
packet loss and two-machine conditions still need separate testing. Different
localizations/keybinds are not admission checks here.

The upstream installed-game startup probe and Documents precedence probe were
not imported; the latter work remains deferred. This tool does not change feeds,
production services, public versions or end-user launch behavior.
