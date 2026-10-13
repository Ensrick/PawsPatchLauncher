# Ensrick follow-up: RWD contribution and recovery review, 2026-10-13

Based on main `93c2f2f662e0414889555a22c84434071b343140`, preserving the uncommitted RWD relocated-path
and documentation follow-ups. No feed, server, public version or installed game changes in this step.

## Contribution status

- PR #8 is already merged at `313ee138de70fe8c3436544f42ab7edfa3071ce5`, verified as an ancestor of main.
- Reviewed PR #1 at `741a648dffbff1d12fb1a4e3c8f10d026f6c0ba2`, including its later note that the 1.3.68 and
  1.3.72 retail archives are identical. The hardened integration retains its reader/writer, CLI commands,
  format research, fixtures and developer/test-project integration, with intentional fixes documented in #8.
- Closed #1 as superseded, following Ensrick's explicit agreement, with a
  [contribution/status comment](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/1#issuecomment-6144235126).
  No branch was deleted and no author history was rewritten. Added explicit origin links to the local RwdTool
  README and corrected the developer guide's stale claim that the launcher does not use RWD.

## Recovery PR #9

Reviewed `f9264f26d413320e40f28a23a5b01cf01992932e`. The implementation is save/lobby recovery, not hot join.
Our published adapter already owns disconnect detection, recovery-save completion and lobby return.
The additions worth pursuing are client save copies, host multiplayer autosave and developer test scenarios.
They should not introduce another owner of recovery, duplicate native call hooks or change our manual-vote policy.

Unchanged upstream build, using the local hash-pinned mapped 1.3.72 image:

- 90 offline recovery checks.
- 130 recovery checks including signatures/call sites in the mapped image (includes those 90).
- 13 installer configuration checks.

### Autosave overlap reproduced offline

The unchanged `autosave_tick` decision queues another `GameSave` when `SESSION_SAVE_TASK` points to a pending
task, and also when its own recovery state is `SAVING`. An isolated test uses PR #9's `reset()` and `chain()`
fixtures, stubbing only engine command dispatch with a counter. With a ready source, a one-minute interval,
`autosaveSince=1000` and `now=62000`, it queues one command in each case:

| State | Commands queued |
|---|---:|
| Idle positive control | 1 |
| Native task present, status 1 | 1 |
| Recovery SAVING, native task not assigned yet | 1 |

The latter two should defer autosave. Coordination must include the queued-command interval before a native
task pointer appears, and our existing adapter's state when composed. This demonstrates a scheduling gap,
not a newly reproduced in-game crash. PR #9 is not integrated or merged.

### Rig review

Static findings in the current scripts:

- `Probe-Precedence.ps1` overwrites `PawsRig/probeA.rwd` and `probeB.rwd` without guarding pre-existing files,
  then deletes them during cleanup. `Test-Startup.ps1` similarly overwrites/deletes `PawsRig/startup.txt`.
  Prefer fresh isolated game/profile directories with owned-path cleanup.
- Rejoin accepts a `Loading File: <Save/MP Resume ...` line without requiring subsequent state 2. Strengthen
  it with confirmed match entry on both peers, a survival interval and scans for assertion/desync/crash markers.
- `addmod` script precedence is useful evidence, separate from our launcher's tested `adddepot` menu-label path.

Sent the [review and split request](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/9#issuecomment-6144441663):
separate the rig/probes, narrow the runtime work to copies/autosave, coordinate saves through one recovery owner.
No upstream rig was run against the user's game installation.

## Local fix from PR #11 / issue #10

Applied `5c1eabfe234d26e7ac94293b6525a58942331dc3` locally to `game/reconnect-test`.
A client that received the match's save can become replacement host with an unloaded source-resource header.
Starting a recovery save then reaches the game's assertion on the ASYNC thread and can time out waiting for its
lock, as reported in [issue #10](https://github.com/VasiliyPaw/PawsPatchLauncher/issues/10).

The preflight follows the game's source-resource chain read-only. If that chain is unavailable, unloaded or
too deep, it queues no save/pause and starts no recovery transition. This does not repair the missing header or
make host migration seamless. The original host and a map-started takeover remain valid positive cases when
their source walk succeeds.

Independent disassembly of the pinned image confirmed the field reads, chain traversal and cdecl/AL type check.
The image pin was not bypassed: `b865d8206990c4f055c51de857f0b09b88ab5ee3b6ae74ee5232134001aa591c`.
Ensrick's attribution is recorded in `game/reconnect-test/THIRD_PARTY.md`.

Our additions to that patch are a diagnostic that also covers missing settings/resources, and a rejection test
for each of the four new code guards in the integrated build.

Validation on this final local source:

- 133 native recovery assertions.
- 24 integrated adapter assertions; 34 generated code guards.
- A mutant removing the new preflight fails the unsafe-save regression.
- All four Pure helper variants build with the new integrated DLL, including deterministic rebuild comparison.
- 804 guarded native composition checks across 12 compositions and three image bases; 477 random-map
  transaction checks; 24 channel/lobby identity checks; 145 AI option checks for each of Pure and Arcane.
- No game run, PC/VM recovery test or full launcher-suite rerun in this step.

Evidence and local build outputs: workspace `outputs/ensrick-review-20261013/` (`native-fixed.log`,
`helpers-fixed.log`, `pr9-native.log`, `mutation-result.json`, `autosave-overlap.log`, `source-walk-1372.txt`).
The local helpers are build artifacts, not installed user launch shortcuts. PR #11 remains open, with a
[validation comment](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/11#issuecomment-6144442015).

## Next acceptance step

Test the local integrated guard in isolated Vanilla and Immortals games: receive a saved match as client,
lose the original host, confirm takeover queues no unsafe save and survives beyond the old failure interval.
Also verify ordinary client-drop recovery on the original host and a map-started takeover. Record local LAN,
PC/VM and Steam/Internet results separately; none is implied by the offline checks above.

The Documents-depot scan/report stays deferred. Localization and personal keybindings are not new lobby
admission restrictions. A subsequent local step implements our side of
[shared save coordination](recovery-save-coordination.md), with deferred-drop and real-DLL export tests.
Autosave/copy integration still requires review of the narrowed PR #9; the user deferred in-game acceptance.
