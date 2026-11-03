# Ignore zero morale and explicit retreat

Revision 1 for Kohan II 1.3.72; released with the 2026-10-29 betas.

## Exhaustion policy

`COMPANY_BEHAVIOR` reads the installed `.pawpatch/state.json`, not unsaved launcher preferences. A missing `appliedSettings.ignoreZeroMorale` is false. True is accepted only for Arcane Wars with the core patch enabled in stable/beta, never data-only or Pure compositions. The launcher exposes the switch only when the selected Arcane Wars feed advertises `companyBehaviorRevision >= 1`.

Enabled ignore policy guards and replaces the entry to CAI's Exhausted transition at RVA `2778C6` with `mov al,0; ret`. This prevents entering Exhausted, including its strategy cancellation. It does not periodically exit/re-enter exhaustion, clear the command, issue another command, or write the morale value. Native callers keep their ordinary fallback behavior. The separate Exhausted update call at RVA `274A1A` returns decision 2 so a company already in that state in a save leaves through native cleanup. An order already discarded before the save cannot be reconstructed by this patch.

Disabled ignore policy retains stock entry plus the existing scoped `ExhaustionRecoveryPatch`. Shared morale decisions, Stun and Rout are unchanged. Both writes use byte guards, readback, instruction-cache flushes and rollback attempts for both locations.

## Retreat priority

The entry gate for native group combat detection at RVA `233035` returns false only when the group's current command definition kind is 7 (explicit Run). All other commands continue through the complete native function. This prevents the combat-support path from replacing Run with guarding another company. Normal formation spacing and group movement coordination remain active.

The gate adds no world writes or RNG calls. Its executable allocation and installed jump are verified and rolled back on failure. It composes with the separate same-target combat cohort patch.

## Compatibility

The option is included in native lobby identity, launcher configuration export/import (`ZM1`), friends' configurations, effective settings, change detection, and applied installation state. Missing and false have identical identities; true differs. Local language and keybind preferences remain outside this setting.

## Validation (2026-10-29)

- Native installer tests: 138 retreat + 173 exhaustion checks, including rollback failures.
- Installed options: 72 Arcane + 72 Pure checks.
- x86 execution: 2,544 cases using the emitted gate and native combat body; command kinds, 1/6/14/64 members, relocation, register/stack behavior, exhaustion entry/exit.
- Launcher: 348 option checks plus 325 existing AI configuration checks.
- Native lobby identity: 101 checks in the complete Arcane build.
- WPF isolated installation: 10 checks for visibility, default off, pending changes, applying on/off and persisted state.
- Complete Arcane runtime and four Pure runtime variants compile; previous feature manifests retained with the explicit added behavior fields.
- Fresh isolated game startup verified in memory with the option on and off. Final QA state is off.
- Live 13-company Run observation: no combat-support/Guard replacement during 28.8 simulated seconds. This does not replace a multiplayer test with a naturally stunned groupmate.
- Live zero-morale move and targeted attack observations retained the same command pointer and destination/target. The attack test subsequently moved toward the target. The stock-enabled control also continued at zero morale in this fixture; these observations establish order retention, not a controlled reproduction of every Exhausted transition.

Evidence and scripts: workspace `outputs/selection-scroll-20261029/`, especially `morale-disabled-moving.json`, `morale-disabled-attack.json`, `retreat-after-group.jsonl`, and `startup-exhaustion-{on,off}.json`.

The Arcane beta advertises this capability with compatible native helpers. Multiplayer peers must use the same option and runtime package. Two-peer Arcane simulation and a fresh recorded replay passed with the option enabled; see ../../docs/release-validation-20261029.md for scope and evidence.
