# Task-status initialization beta release

Arcane Wars **0.4.2-beta.5**; Vanilla/Immortals **0.4.1-beta.7**, runtime
**1.3.72-pure.15-beta.7**. Source commit:
`9c49b9650433e4291b4f8c4cb679ebe091ab6701`.

The repair initializes the native AdminNode task-status field before entering a
network session. It addresses the captured host-departure assertion at its
producer. Enum/renderer assertions and received-save guards remain enabled.
See [root cause and network evidence](verification-task-status-2026-10-23.md).

## Package scope

Seven archives were rebuilt from the signed public widescreen-beta baseline.
Only the eight Arcane/four Pure helper binaries and Arcane version INI changed;
all other payload bytes are verified identical. Pure data/common archives have
updated package identities but identical payloads. Arcane source recipe, mod
content, language/keybind behavior and launcher version remain unchanged.

Both **feed/v3/beta.json**, used by current launcher configuration, and
**feed/v2/beta.json** receive the new seven package records. All stable and
legacy catalogs remain unchanged. The preparation/promote/readback tool checks
the configured active URL, preserves unrelated records separately in each
catalog, verifies signatures and checks public assets before promotion.

## Validation

- 417 installer/rollback checks and 94 native-instruction cases from the repair
  audit remain applicable: all candidate source hashes were rechecked.
- Four Pure variants rebuilt deterministically; 828 guarded hook installs in
  12 compositions, 477 random-map transaction checks, 24 lobby-channel checks,
  and 12 received-save guard compositions passed. Both AI profiles passed 145
  option checks each.
- Eight Arcane variants built and passed five existing offline self-test modes
  each. All 12 feature reports match the accepted repair candidate except the
  intended release version identifiers.
- **21 installation/rollback cases passed** in the isolated
  `D:/PawWidescreenReleaseQA-20261020` fixture: 16 Pure cases including six
  languages for each mod, three Arcane AI on/off/on cases, stable rollback and
  patch disabling.
- **Three full-patch native menu launches passed**, one for each mod. Every
  retained helper log includes `TASK_STATUS_INIT r1`. Test processes were
  terminated after menu observation; these are not full-match acceptance runs.
- The earlier isolated network series passed **63 checks in six runs** for
  takeover, saves, old-save loading and launching/saving after takeover. It used
  stock Vanilla plus multiplayer-saves r2 and this repair on one PC. Full-patch
  two-machine network recovery and direct hot-join remain separate work.
- Source CI passed:
  https://github.com/VasiliyPaw/PawsPatchLauncher/actions/runs/39999187872.
- Active-catalog regression passed 64 checks using the launcher's signature
  verifier, package selection, displayed-version resolver and update detector.
  All three mods offer the update from the previous v3 beta in six languages.

ModDB rejected the test runner's automatic source download with HTTP 403. The
Arcane installer tests resumed using the previously downloaded, signed-catalog
verified assembled archive in the isolated test cache:
`E562BE5AF62752D61DB27306B4C7623CF30C37F916EB199DF9C5E4CFA0F9FE19`.
No public source recipe or download policy was changed.

Evidence: workspace `outputs/publish-task-status-20261023`, including preparation,
per-file payload audit, builds, menu helper logs, installer summaries and signed
test feeds. The original game executable remains unchanged in the fixture.

## Published assets

Both immutable prereleases reference the source commit above. All seven uploaded
digests were verified:

- https://github.com/VasiliyPaw/PawsPatchLauncher/releases/tag/patch-0.4.2-beta.5
- https://github.com/VasiliyPaw/PawsPatchLauncher/releases/tag/patch-pure-0.4.1-beta.7

Catalog promotion, update-detection checks and public readback are retained in
the same evidence folder. Availability is confirmed only after that readback.
