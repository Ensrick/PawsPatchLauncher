# Contributor proposals: implementation order

Reviewed against GitHub on 2026-10-17. This is a proposed sequence, not approval
to merge all changes. Current publication is limited to launcher diagnostics and
the independently investigated animation Stop crash guard.

## 1. Establish repeatable multiplayer regression tests

[PR #12](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/12) supplies a
developer-only two-copy LAN rig: save copies, autosaves, hosting a received save
and original-host departure. Review process ownership, cleanup, isolated user
data and failure reporting against current main before integrating it. Run one
pair at a time, without VM recording. Follow with two real machines; one-PC
success is not evidence for all network conditions.

The received-save source guard from [#9](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/9)
already covers the failure described in [issue #10](https://github.com/VasiliyPaw/PawsPatchLauncher/issues/10).
Retain it as a regression case rather than introducing a duplicate fix.

## 2. Investigate terrain texture pressure with the extended camera

[PR #16](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/16) increases the
terrain texture pool from 120 to 1024 entries, approximately 8 to 64 MB. The
reported reduction in terrain offloads makes this worth testing first among
player-facing proposals. It does not yet prove that texture churn causes the
friends' NVIDIA driver crashes.

Compare the original and proposed pools while moving the camera at maximum
zoom, on large maps and in long matches, including lower-memory configurations.
Check all three mods and preserve their own UVars settings: do not replace an
Arcane Wars configuration wholesale with a stock file. Measure memory, stutters,
offloads and crashes; then test multiplayer and use a beta rollout if beneficial.
This concerns terrain resources, not a new game-icon pack.

## 3. Reproduce host-departure assertions and desync recovery

[PR #7](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/7) now suppresses only
two network enum-read assertions, rather than globally ignoring assertions.
Its author reports improved HostLeaves results. However, skipping a dialog does
not establish that the invalid enum is safe to use. Trace the invalid value and
its consumers, then prefer rejecting the invalid message or a controlled peer
disconnect if possible. Do not merge unconditional continuation merely because
the immediate freeze disappears. Preserve rendering/driver assertions.

Use the rig to investigate [issue #15](https://github.com/VasiliyPaw/PawsPatchLauncher/issues/15):
save, return to lobby, reconnect affected clients, then reload. Confirm state
synchronization after reload and repeated recovery. Our first-error-only sync
log already addresses repeated diagnostic writing; avoid adding it twice.
Direct entry into a running simulation remains a separate research task.

[PR #2](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/2) provides useful
reconnect notes for 1.3.68. Keep version-specific addresses explicitly labelled
and revalidate on 1.3.72. Documentation alone is not a completed reconnect feature.

## 4. Review the custom-mod manager as a separate launcher update

[PR #17](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/17) proposes a
dedicated mod-manager page, game selection cards, load-order/overlap indicators,
replacement of an existing RWD and apply/undo controls. The overlap and replacement
handling are useful; the larger navigation change needs a visual review against
the current game-settings workflow before adoption.

Verify cancel, replace, interrupted install, rollback, compatibility and all six
languages. Test actual RWD mounting and multiplayer, not only the UI. Existing
RWD import/order already implements part of [issue #5](https://github.com/VasiliyPaw/PawsPatchLauncher/issues/5).
Consider ZIP import and explicit dependency/conflict metadata afterward.
Translation, voice and keybind differences must not become multiplayer blockers
unless an actual simulation-affecting difference is demonstrated.

## 5. Consider packaging and distribution improvements later

[Issue #6](https://github.com/VasiliyPaw/PawsPatchLauncher/issues/6) proposes
patch/delta packages built against the user's own archives. Potentially useful
for download size and avoiding redistribution of original data, but requires
exact base hashes, versioned patch recipes, output verification, rollback and a
safe response to a Steam update. Treat it as a separate installer-format project.

[Issue #4](https://github.com/VasiliyPaw/PawsPatchLauncher/issues/4) proposes
packing a loose Documents depot into an RWD. Only consider an explicitly selected
folder with a preview and backup; do not automatically move or package user data.
The Documents compatibility work in [PR #3](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/3)
was explicitly deferred by the owner and remains unpublished. Do not reactivate
it as an implicit dependency of the mod-manager work.

## Already integrated

Gold launcher icons (#13), integrated widescreen HUD work (#14), RWD support
(#8, superseding #1), and multiplayer saves/source protection (#9/#11) are already
represented in the project. Keep regression coverage; do not schedule them as
new features. No additional open icon PR was present at this review.
