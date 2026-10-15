# RWD, plugin API and reconnect: collaboration starting points

This note records the original contribution plan, not a fixed API contract.
The dated RWD status below reflects subsequent work; the API and reconnect sections retain the research goals.
Keep each topic in its own branch/PR so a useful RWD tool does not depend on
unfinished multiplayer research. Start with a small runnable example, then agree
on integration based on evidence.

## 1. RWD packing and mod load order

The launcher currently distributes ZIP modules with `module.json`, per-file hashes
and an ordered filesystem overlay. Start by reading
[`PawsPatchPublisher`](../tools/PawsPatchPublisher/Program.cs),
[`ModuleInstaller`](../src/PawsPatchLauncher/ModuleInstaller.cs), and
[`GamePackageSelector`](../src/PawsPatchLauncher/GamePackageSelector.cs).

First PR: a standalone RWD pack/list/extract demonstrator with tiny, self-authored
fixtures and format notes. Include dependencies, license, exact commands and
repeatable output. No game installation should be required for format-only tests.
Status, 2026-10-13: [`tools/RwdTool`](../tools/RwdTool/README.md) covers the format items below.
The [custom RWD manager](custom-rwd-mods-local.md) and its cancellation fix shipped in launcher 0.9.3;
its `adddepot` menu-label probes and install/rollback checks are documented there. Ensrick separately
reported `addmod` script precedence and case probes. [Current format evidence](rwd-format-evidence-20261013.md)
keeps those loader paths distinct. Texture/audio case behavior and semantic equivalence between differently
packaged multiplayer content remain unverified. The [relocated-path follow-up](rwd-relocated-paths-20261013.md)
is local only. The cancelled Documents-depot scan/report remains deferred; this follow-up adds no admission
rules for localization or personal keybindings.

Original integration checklist (use the dated status above for completed items):

- Correct path normalization, encodings, empty files, duplicate names and case
  handling; reject traversal paths and malformed or excessive sizes.
- Round-trip verification and deterministic output for identical inputs, or an
  explicit explanation of variable metadata.
- The engine's actual load precedence with two archives containing the same test
  file, and with a loose file override. Do not infer it from archive filenames alone.
- Installation, disabling, reordering and removal preserve ownership, original
  files, hashes and rollback. Prefer versioned output rather than editing a signed
  downloaded archive in place.
- Multiplayer peers can compare the effective content and load order. Packaging
  the same filenames does not establish equivalent simulation data.

Further changes to manifests or UI should remain separate from format research.
Keep current package installs working during the transition.

## 2. Plugin API

There is no committed general-purpose native plugin ABI in this repository. First
define whether the proposed interface runs inside the game, in a helper process,
or in the launcher. A launcher API and an in-process game API have different
lifetime, threading and failure behavior.

An initial proposal should specify supported game builds, a versioned handshake,
plugin lifecycle, event ordering, error handling and a read-only sample. Separate
observational calls from commands that change simulation state; those commands
must follow a documented multiplayer authority/order model. Verify runtime hook
compatibility with existing Paw's Patch hooks rather than replacing them blindly.

Share original plugin/adapter code and observations about the executable. Do not
commit a full game EXE or a complete decompiled game source tree. A decompilation
is a research input, not proof of a stable API or permission to relicense the game.

## 3. Rejoining an active multiplayer match

Status, 2026-10-13: our [Pure beta integration](pure-recovery-integration-20261013.md) implements recovery through
a save and lobby. The [PR #9/#11 follow-up review](ensrick-followup-review-20261013.md) records a locally tested
save-source guard and the remaining autosave/copy work. Our side of
[save coordination](recovery-save-coordination.md) is also implemented locally; upstream composition and live
acceptance remain pending. Direct hot join remains a research goal.

The existing native save-transfer tuning in `game/fast-transfer/` is for lobby
save transfer. Continuing after a desync also does not restore a disconnected
player or reconcile divergent game state. Neither is a completed reconnect system.

Original first-PR scope: an opt-in research demonstrator, reproducible scenario, and design note.
Document what works and what still crashes. Do not enable it in release feeds yet.
Observations from 1.3.68 (drop handling, votes, back-to-staging, a host crash) are in
[reconnect-research-1368.md](reconnect-research-1368.md).

Suggested progression:

1. Reproduce one client disconnecting from a two-peer game; record the exact builds,
   player/observer roles and failure logs on both peers.
2. Define who pauses and owns the authoritative snapshot, how the returning player
   is identified, and how its old slot is reclaimed without accepting a wrong player.
3. Restore a consistent simulation snapshot, sequence/tick, random state and pending
   commands before resuming. Explain how stale/duplicate messages are handled.
4. Compare state checksums at the same simulation points after resuming; verify AI,
   construction, combat and economy continue consistently on both peers.
5. Test repeated reconnects, timeouts, a failed transfer, mismatched components,
   observers and saves/replays. Either support host disconnect explicitly or reject
   it cleanly; client reconnect does not imply host migration.
6. Test on real separate PCs as well as VMs, then integrate as an explicit beta option
   with a defined failure/rollback path.

Report automated, VM and real-peer results separately. Use small sanitized logs and
test assets; keep personal saves, dumps and credentials out of public PRs.
