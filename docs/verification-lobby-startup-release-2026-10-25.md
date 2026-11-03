# Lobby initialization beta delivery — 2026-10-25

Source commit: `5c2baa5655e1a3e194dc3fe53442b7ba1b7eda6f` (includes fix `e1dc507`).
Release targets: Arcane Wars **0.4.2-beta.7**, Vanilla/Immortals **0.4.1-beta.9**;
Pure runtime `1.3.72-pure.15-beta.9`.

## Release gates

- 21 installation/rollback cases and three full release menu starts passed.
- Seven lock scenarios and existing native recovery/coordination suites passed.
- All 12 helpers carry the expected tested native DLL; versions/features and all
  seven immutable package hashes were checked.
- Prior fix verification also recorded 18 repeat starts; see
  [lock mechanism and limits](verification-lobby-startup-2026-10-25.md).
- Source GitHub workflow **40205664611** passed before publication.

## Live network regression with release payloads

- **vanilla**: 18 checks; all passed.
- **immortals**: 23 checks; all passed.
- **arcane**: 11 checks; all passed.

Vanilla and Immortals each completed two client-crash → native save → lobby →
byte-identical transfer → reload cycles. Game time advanced on both peers after
each recovery. After the original host crashed, the replacement host rejected
an unavailable received-save source and remained alive for the 70-second check.
A client-created manual save copy was successfully hosted, transferred and
loaded by a new pair; unsafe copying from a received save was skipped safely.

Immortals additionally used English/Russian installations and different F12/F11
bindings. Compatibility tokens matched. A host autosave and client copy completed
at the shipping five-minute interval. Vanilla's timer was not retested in this
series; both use the same verified Pure runtime.

Arcane passed two-peer simulation, host save, survival after client loss, normal
exit, lobby takeover after host departure, replacement-host launch, simulation
and save. Arcane does not include Pure's automatic recovery module.

No assert/desynchronization messages were found in the completed scenario logs.
These were two isolated local LAN processes, not a test over a remote WAN or VM.
QA-only launch/profile adapters were used; production helpers keep their existing
single-game launch guard. No VM recording or modifications to the user's installed
game were performed.

## Evidence and publication

Workspace: `outputs/publish-lobby-startup-20261025/` (`validation.json`,
`network-results.json`, resource checks, installation/menu logs, staged package
audit and signed catalogs). Network fixtures: `G:\PawLobbyQA-20261025`.

The publication tool verifies immutable assets before promoting both active v3
and v2 beta catalogs. Stable and legacy catalogs and launcher payloads are held
unchanged. Final delivery additionally requires public readback, exact configured
feed URL verification, and the 64-case update detection check.
