# Attainable group movement threshold (local candidate r2)

## Reproduced defect

In the isolated 1.3.72 QA match, all 14 companies retained their Move orders
and group membership. None of their 126 units changed position over 11.125
simulation seconds. The slowest company had natural speed 0.9; synchronization
assigned the other companies speeds between approximately 0.429 and 0.995.
All were below the native `Group` minimum movement threshold, 1.0.

The same threshold is enforced in three native places:

- RVA `26F69E`: company movement update, before retry/replanning.
- RVA `27DC2C`: unit movement speed setter; below threshold stops movement.
- RVA `27F499`: unit movement update; below threshold waits.

Thus the slowest company could not start, and its waiting peers never received
a higher synchronization speed. Formation expansion was not the cause of this
threshold defect; it also applies to smaller groups containing a slow company.

## Change

At each of the three already group-gated sites, use
`min(configured minimum, company's current natural speed)` for the native
comparison. A unit resolves its company through its existing membership
component. The cap is used only for finite, strictly positive speeds.
Missing AI/actor/membership/owner/organization and invalid speed retain the
original threshold. Companies at or above the configured minimum are unchanged.

The hook neither raises actual movement speed nor disables normal formation
waiting. A follower assigned a speed below its attainable threshold still
waits. Zero-speed waits, native tiny-speed handling, path retry limits,
commands, collision handling, and formation offsets remain native.
No timers, orders, coordinates, random values, or global settings are written.

Integration uses the existing opt-in `-GroupAttackRecovery` build switch, with
an independent `groupMovementSpeedRevision:1` feature marker. The separate
same-target attack fix remains revision 1. All three signatures are validated
before mutation. Partial installation restores each attempted site; a cave is
retained if rollback cannot be verified, preventing a dangling jump.

## Validation, 2026-10-29

Workspace artifacts: `outputs/selection-scroll-20261029/`.

- `group-saves/group-movement-wait.RSG` preserves a private QA autosave.
- `wait-units.json`: 126/126 units stationary for 11.125 simulation seconds.
- `speed-fix-units.json`, `speed-fix-group.jsonl`, `speed-arrival-group.jsonl`:
  installed the exact C# payload into PID 29188, image `00390000`, cave
  `02EE0000`, without issuing an order. All 14 companies resumed; each reached
  within 0.5 map units of its own destination and completed its Move order.
- A subsequent UI movement order retained the extended formation flags.
- `speed-stop-group.jsonl`: after UI Stop all 14 company orders remained null
  for more than 40 simulation seconds. No old order restarted.
- `speed-native/live-summary.json`: per-company arrival distances and summary.
- 432 installer assertions: three image bases, mismatching signatures, every
  fallible operation, partial writes, and uncertain rollback.
- 5,292 emitted-x86 assertions across three sites and image bases: slow and
  normal companies, follower waiting, zero/negative requests, invalid caps,
  low configured thresholds, direct-company/unit-member paths, missing links,
  registers, SIMD state, native comparison flags, and stack-only writes.
- Combined helper built and staged in the private QA copy. SHA256:
  `94ddcab3fff582aac01cc1f1910d992e56f24ba01cc5cc678c0e4070563ab8e2`.
  Live installation was verified; a fresh process startup with this combined
  helper has not yet been retested.

No public catalog or package was changed. Multiplayer and replay validation
remain required before distribution, including incorporating these simulation
changes in the shared compatibility identity. This fixes the observed speed
deadlock, not every possible stopped-company or failed-path case.

## Revision 2: wait only during active coordinated movement

The next live report selected zombie company 10639 attacking gate 2061.
Its attack remained present, but a resolved group handle alone enabled three
native formation-speed waits even when group 34453 had mode 0, no leader and
no command. The company eventually fought without another order; this case
was a prolonged delay, not permanent deletion of its attack command.

Revision 2 replaces the three group-presence calls at RVAs `26F68A`, `27DC18`
and `27F485`. For a real GGroup, waiting requires mode 1 and a non-null group
command. Idle membership (mode 0) and combat support (mode 2) do not impose
formation waiting. Unexpected actor types retain the old true result. Native
registry lookup still validates the complete generation handle; null and stale
handles return false. Revision 1's attainable-speed caps remain unchanged.
The three wrappers preserve the native call ABI and do not write simulation
data or create commands. All six sites are guarded and rolled back together.

### Revision 2 validation, 2026-10-29

- Private `group-saves/attack-stale-speed.RSG` was loaded twice, first with r1,
  then with the exact C# r2 installer applied live. Initial observed states in
  `attack-reload-initial.json` and `attack-r2-initial.json` were identical.
- `attack-reload-old-early.json`: first company movement 8.5 simulation seconds
  after the captured initial state. `attack-r2-early.json`: movement within
  0.5 seconds, with the same gate attack target retained.
- `attack-r2-combat.json`: Engage state and gate damage, HP 3247.4 to 2764.117.
  A stationary company center during this interval does not imply idle units.
- Loading this fixture reports a lake-fish object warning, acknowledged in
  both runs. The warning is separate from the movement fix.
- `r2-formation-group.jsonl`: 224 samples, 44.1875 simulation seconds. All 14
  companies complete their existing coordinated movement and finish within
  0.5 map units of their own native Move destination (CAI +0x84/+0x88), not
  merely the shared group-center destination.
- `r2-stop-before-group.jsonl` / `r2-stop-after-group.jsonl`: company 885 given
  a UI Move, then UI Stop. Its command stays null for 11.6875 simulation
  seconds across 60 samples; company-center drift is zero. The earlier r1
  all-14 Stop test above is historical evidence, not an r2 all-14 Stop test.
- 1,098 installer assertions and 7,092 emitted-x86 assertions pass, including
  all six sites, relocation, failure rollback, active/inactive group modes,
  null commands, unexpected actor types, and native stale-handle rejection.
- Live PID 29188, image `00390000`, r2 cave `02EF0000`; installation verified.
  Combined helper rebuilt and staged in the isolated QA game, feature
  `groupMovementSpeedRevision:2`, SHA256
  `a5ff1d257473b1a2020f44eeee8d29b4fb58ca0b8f96ea7a5931079a19ac2192`.

Game left paused. No public package/catalog changed. Fresh-process startup
of the combined helper, multiplayer/replay validation and shared simulation
compatibility identity remain release gates. This is a fix for the observed
formation waiting conditions, not a claim to resolve every attack/path failure.
