# Graphics recorder lifecycle repair — 2026-10-25

Local repair only. No packages, catalogs, installed user game, or public releases
were changed. Candidate helper versions retain the current beta labels; they are
test builds with `builtInGraphicsDiagnosticsRevision=3`, not new releases.

## Reproduction and cause

The unchanged r2 recorder was reproduced hanging after a normal Arcane exit in
the private `G:\PawNetworkQA-20261025\arcane-r1\host\game` fixture. Target PID
25848 and its helper exited; recorder PID 11828 remained alive. The x86 minidump
shows its caller at recorder `0x401868`, immediately after the call to
`WaitForDebugEvent(...,1000)`. The old timeout branch unconditionally retries,
with no target-process exit check. No EXIT_PROCESS event was logged.

Contrary to the initial network report, the logs were not empty. Reading them
returned START/MODULE/ATTACHED/ARMED; directory metadata for the still-open files
had reported length zero. No logger rewrite was needed.

The exact engine/OS path losing the debug exit notification is not established.
The confirmed recorder bug is its unbounded wait after the target has exited.

## Change

After an empty/failed debug-event wait, r3 checks the already-open, identity-checked
target process handle. If it is signaled, it records `TARGET_EXIT_HANDLE` and exits
normally. It never reopens the target by PID. A living idle game remains monitored.
Pending events, exception propagation, capture conditions and safe debugger exit
remain unchanged. No target termination or gameplay patch was added.

This uses the documented process wait support in
[WaitForSingleObject](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-waitforsingleobject)
alongside the existing bounded
[WaitForDebugEvent](https://learn.microsoft.com/en-us/windows/win32/api/debugapi/nf-debugapi-waitfordebugevent).

## Verification

All evidence is under workspace `outputs/recorder-lifecycle-20261025/`.

- **13 native cases passed**, including four new lifecycle cases: idle target,
  lost debug-event stream, forced target exit, and forced exit after losing the
  event stream. The lost-stream tests deliberately detach after ARMED, then keep
  the target alive before it exits. This test-only switch is absent from the
  production recorder. Existing identity rejection, first/second-chance handling,
  invalid-call capture, original crash filter and recorder-exit safety cases pass.
- All four generated crash dumps contain the fixture's full-heap marker. The
  original unhandled exception filter still runs; killing the recorder still
  leaves the live fixture running.
- **12 helper variants built**: 8 Arcane and 4 Pure. Feature dictionaries match
  the published composition except for diagnostics revision 2 → 3. Source-input
  hash comparison permits only `GraphicsDiagnostics.cs`, `BuildFeatures.cs`, and
  `PureBetaRuntime.cs`; existing native gameplay payloads remain unchanged.
- The 8 Arcane helpers passed their 5 offline startup/UI/lobby/assistant/transfer
  self-tests each. Resource extraction verified the same production recorder in
  all 12 helpers and the additional private Arcane runner.

Production recorder SHA-256:
`0ECA4EAB7BE4F2FAF1E7C9972CF3D80981560F5DC8F5B597C713B35BBEA9CA2E`.

Six successful real-game lifecycle runs, sequentially with one game at a time:

| Mod | Normal exit: recorder delay | Forced exit: recorder delay |
| --- | ---: | ---: |
| Arcane Wars | 0.906 s | already exited at first poll |
| Vanilla | 1.015 s | already exited at first poll |
| Immortals | 1.000 s | already exited at first poll |

Each run verified ARMED while both processes were alive, normal recorder exit,
and absence of an unexpected dump. Normal exits used the new handle fallback;
forced exits retained the ordinary EXIT_PROCESS event path. Delays are polling
measurements, not hard scheduling guarantees. Tests used a game script returning
from the menu, not multiplayer matches. Real exception capture was tested using
the native fault fixture; force termination is not an access-violation crash.

The Pure runs invoke the candidate packaged composition through InstalledHarness.
Arcane uses the prior private startup/profile adaptation, with production
ReleaseStartup discovery retained for this single-process test. No VM or video
recording was used. Test helpers/scripts were restored after every run. Final
inspection found no remaining test games, harnesses, fixtures or recorders.

## Failed setup attempts and limits

- The first Arcane candidate self-test lacked its palette INI in the new output
  folder. Copying the unchanged release INIs resolved the setup error.
- The initial private Arcane multi-process startup filter read MainModule too
  early and failed before starting diagnostics. The serial test then retained
  the unchanged production startup discovery, which already handles that state.
  This does not repair the separate multi-process QA filter.
- One Immortals forced-exit attempt failed in
  `PawLobbyCompatibility.Installer.Install` with `Lobby hook installation did not
  complete`, before `GraphicsDiagnostics.Start`. It was cleaned up and retained
  as `immortals-forced-startup-failure.log`; the next attempt passed. This is not
  counted as a successful run or explained by the recorder repair. Its cause
  remains a separate startup investigation.

Primary evidence: `old-worker-x86.dmp`, `stack-disassembly.txt`,
`native-tests/verification.json`, `dump-heap-check.json`, `live-results.json`,
`resource-check.log`, candidate `features.json` files, and individual recorder logs.
Local build and live-run recipes are `build_candidates.py` and `live.py` in the
same output folder. Future publication must rebuild/embed r3 rather than reuse
the r2 recorder from earlier native artifact folders.
