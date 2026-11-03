# Lobby recovery initialization: lock-safe commit — 2026-10-25

Local fix, not published. Current public versions remain Arcane Wars
`0.4.2-beta.6` and Vanilla/Immortals `0.4.1-beta.8`. The installed user game and
catalogs were not changed. Local candidate labels are unchanged and must not be
distributed as replacement assets for those releases.

## Evidence and limits

The prior recorder lifecycle test failed once during Immortals startup with
`Lobby hook installation did not complete.` The failure preceded recorder
startup. Its original log is in workspace
`outputs/recorder-lifecycle-20261025/immortals-forced-startup-failure.log`.

Twelve ordinary repeat starts with the old recovery payload all succeeded; no
new failing game dump was captured. Therefore the precise lock owner in that
original failure is not established.

A separate x86 reproduction using the actual integrated installer and a
synthetic, relocated guard-valid image **does reproduce a lock dependency**:
hold the process heap lock on one thread and invoke the old recovery installer
on another. It does not finish while the lock is held. Instrumentation stops
at `GetFileAttributesW(config->data)` (phase 101). Releasing the lock immediately
allows installation to succeed. The recorded result is
`mode=heap while_locked=258 phase=101 result=0`.

This matches a real hazard in the shipping handshake: `NtSuspendProcess`
pauses all existing game threads, resumes only the install worker, and waits
five seconds for it. A paused owner cannot release the heap lock. Loader and
PEB lock probes did not block the old **commit** on this machine; waiting for
thread termination under the loader lock would misleadingly count normal
DLL_THREAD_DETACH waiting, so tests observe an explicit completion flag.

## Change

- `PawRecoveryPrepare` validates paths and initializes the recovery critical
  section while game threads still run, before the queued worker advertises
  readiness. It does not patch game instructions.
- During the paused commit, `PawRecoveryInstall` requires the exact prepared
  configuration, rechecks original instructions/imports, and installs hooks.
  Path queries, critical-section initialization, formatting and file logging
  are excluded from this interval. Numeric failure results remain available
  to the managed installer; no failure is ignored or treated as success.
- Readiness messages are emitted once on the first eligible main-thread
  counter callback after game threads resume. Counter forwarding, last-error
  preservation, recovery commands, save format and compatibility token rules
  remain unchanged.
- The standalone reconnect plugin keeps its original initialization behavior.
  Arcane does not include Pure's integrated recovery module; its shared lobby
  worker still uses the same ready/go/result contract.

This fixes a confirmed installation deadlock mechanism. It does not establish
that every possible cause of the original timeout has been eliminated, nor does
it implement direct joining of a running match.

## Verification

Evidence: workspace `outputs/lobby-startup-20261025/validation.json` and adjacent
build recipes, manifests and logs.

- Seven new native scenarios pass: unlocked installation; held loader, PEB and
  process heap locks; changed instruction guard, invalid counter target and
  changed prepared configuration while the heap lock is held. Error scenarios
  return 66, 64 and 68 respectively without hanging. Successful cases also
  exercise deferred logging, the original performance counter and last-error
  preservation after unlocking.
- Existing native suites pass: 133 recovery checks, 24 integrated adapter checks,
  103 save-coordination checks, and 2,613 lobby codec/ABI checks.
- All 12 full candidate helper variants compile. All 40 Arcane helper self-test
  invocations pass. The initial local palette self-test required copying the
  normal `paws_player_colors.ini` companion file beside these candidates; it
  then passed with the complete package inputs.
- **18 real process starts pass**, six each in private Vanilla, Immortals and
  Arcane fixtures under `G:\PawNetworkQA-20261025`. Each reaches
  `PACKAGED_HELPER_READY`; Pure recovery readiness logging also resumes.
  Test-only launch/profile adapters match the fixtures' existing beta labels.
  Production startup discovery and one-game guards remain unchanged.
- Runs are serial, with free-memory/commit guards and verified process handles.
  No VM recording was used. Fixture helpers and command scripts were restored;
  helper hashes match the saved originals. No game, harness or recorder process
  remained after the test series.

These starts and native tests are not a new end-to-end multiplayer recovery
match test. That remains the next network-validation step.
