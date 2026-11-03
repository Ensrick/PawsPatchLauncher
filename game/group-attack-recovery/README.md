# Group attack recovery r1 (local candidate)

The same opt-in build now includes the separately versioned
[attainable group movement threshold fix](MOVEMENT_SPEED.md). It addresses
the reproduced 14-company Move stall without changing this attack-cohort hook.

## Defect and scope

Kohan II 1.3.72 `GGroup::HasCombat` (RVA `233035`) excludes a member when
`memberEntry+0xD` is zero. This formation-cohort classification is cached;
an excluded member can be fighting the group's explicit attack target while
the active group fails to enter its native combat-support mode. Members whose
individual movement orders have already failed can remain asleep, although
the group's attack command is still present.

The hook at RVA `233063` admits an otherwise excluded member only when group
mode is 1 and both commands are native kind 5 (`move`), contain target+attack
flags (`0x41`), and reference the same nonzero full target ID. All native
combat and Guard checks following the gate remain intact. Existing eligible
members keep the original path. No orders, retry timers, coordinates,
simulation constants, or random values are written by this hook.

This fixes the reproduced group support deadlock, not every possible pathing
failure. A separate observed native path-failure mechanism clears a member's
order after repeated movement attempts; that limit has not been removed.

## Integration

Opt-in `game/beta7/build.ps1 -GroupAttackRecovery`; compile flag
`GROUP_ATTACK_RECOVERY`, feature `groupAttackRecoveryRevision: 1`.
Startup validates surrounding native bytes before installing while the game
is suspended. Payload uses one RX page. Partial-install rollback restores
native bytes; uncertain rollback retains the page to avoid a dangling jump.

This affects simulation. Do not mix enabled and disabled peers or claim old
replay compatibility. Before distribution include it in the common gameplay
compatibility/version identity and validate two-peer and replay behavior.
No feeds, published packages, or version numbers were changed for this work.

## Evidence, 2026-10-29

Artifacts: workspace `outputs/selection-scroll-20261029/`.

- Preserved original saves in `group-saves/`; private QA install only.
- Full native `HasCombat` emulation on the paused game: original/patched
  return true with cached cohort=1; with cohort=0 original returns false,
  patched returns true (`group-emulation.json`).
- Controlled live fixture: loaded `group-before.RSG` (preserved Autosave6),
  then changed only member 6970's cohort byte from 1 to 0. Load reconstructs
  that byte as 1. Therefore this is a reconstructed exclusion condition,
  not an assertion that the pre-load byte was recorded in the original stall.
- Unpatched: nine members Sleep/null commands and unchanged coordinates at
  1640.8125 and 1695.1875 simulation seconds. Group mode1, attack target72.
  `gate-before-group.jsonl` continuously covers the first 20.7 game seconds;
  `gate-pause-group.jsonl` and the first `gate-after-group.jsonl` row confirm
  the same state at1695.1875.
- Installed candidate while paused; resumed without player orders. At
  1696.1875 group entered support mode2; at1696.4375 eight members were moving;
  by1706.375 all nine had commands and were moving. At1750 all nine remained
  commanded and had advanced towards the target. See `gate-after-group.jsonl`
  and `group-ab-summary.json`.
- 159 transaction assertions: signature mismatches, three image bases,
  failure at every installer operation, partial writes, uncertain rollback.
- 204 x86 assertions execute bytes emitted by the C# installer: same-target
  attack, existing cohort, idle/support group, null commands/AI, different
  targets, Guard/other kinds and movement-only flags; integer registers,
  stack, FPU control/MXCSR, and no world-state writes; three relocations.
- Live Stop regression: selected all ten group members and clicked Stop.
  All ten left the attack group and had null commands through the recorded
  1692.75--1722.1875 interval; the old attack did not restart
  (`stop-after-group.jsonl`). Temporary observation hooks were removed.
- Fresh combined-helper startup succeeded in the isolated QA copy. Readback
  matched the C# emitted payload exactly; all four temporary observer vtable
  slots were native again (`group-startup-verification.json`). Helper SHA256:
  `a8c72a1c53143eabafec0ffb0734ceec6de9080c5a6b020507a3393c1f9ed895`.

Two-peer multiplayer and replay acceptance are still outstanding. The QA
helper also contains previously accepted selection-scroll r2; it is local,
not a new published beta.
