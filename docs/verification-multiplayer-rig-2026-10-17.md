# Local multiplayer rig integration — 2026-10-17

Imported the core of Ensrick PR #12 at
`c94cd8f85f4a4a7520272276aa51f79990030f0c`, retaining the MIT attribution.
No production modules, launcher behavior, feeds or public release versions change.
Installed-game startup and Documents-precedence probes were not imported.

## Local integration

`D:/PawsMultiplayerRig` contains the compiled rig, isolated stock Vanilla fixture,
configuration and an interactive launch menu. Preparation copies only the selected
stock runtime and pinned loose files/skins. Original game/Documents are not test
destinations. Fresh run profiles and retained logs prevent stale saves from
satisfying a later test. The fixture refuses nested redirects and concurrent runs.

Each test process starts suspended and is assigned to a kill-on-close Job Object
before resuming. Live process handles own cleanup; injection verifies the exact
path, hash and creation time. Available physical/commit memory is checked before
starting either copy. No VM recording is used.

## Evidence

- Multiplayer-save native checks: 79; 105 with the mapped 1.3.72 image.
- Fast-save completion checks: 25; managed installer checks: 14.
- Disposable process/path safety suite: 10 passed, including owner termination
  automatically terminating its child and refusal to reuse previous evidence.
- Private Documents shim: eight checks passed, covering Unicode/ANSI calls,
  Unicode paths, and refusal to fall back to real Documents for missing/oversized
  private paths.
- Injected setup failure stops the test host and removes the temporary script/DLL.
- Real SaveCopy run: all seven upstream checks passed. Host and client joined a
  LAN match, the host wrote `Rig Test.RSG` (231,295 bytes), and the client wrote
  `Rig Test (copy).RSG` (231,313 bytes). The run also produced `MP Autosave A` and
  its client copy. Both game logs closed with `-ok` without assertion, desync,
  unhandled-exception or mutex-timeout markers. Both processes exited.
- The final report's stricter exact-log lookup, normal-host-exit and nonempty-save
  checks were rechecked against these retained files. Normal exit also removes
  the generated fixture scripts and shfolder.dll.

SaveCopy evidence: `D:/PawsMultiplayerRig/runs/20261017-070100-518-SaveCopy-a0bc14`.
The run used the positive Unicode-folder path; subsequent shim validation added
the ANSI/fail-closed behavior above. The final setup-failure check uses that shim.

This is stock Vanilla plus the current multiplayer-save module, not the full
Paw's Patch or recovery-to-lobby module. The dedicated two-autosave, LoadCopy and
HostLeaves scenarios are available but were not newly validated end-to-end here.
An incidental first autosave is not a complete two-autosave test. No direct
hot-join, Steam disconnect-vote or two-PC network acceptance is claimed.
