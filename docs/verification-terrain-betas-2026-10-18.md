# Terrain texture pool beta release

Arcane Wars: **0.4.2-beta.3**. Vanilla and Immortals share **0.4.1-beta.5**,
runtime **1.3.72-pure.15-beta.5**. This adopts Ensrick's PR #16 overlay, with
attribution retained in `game/terrain-texture-pool/LICENSE.Ensrick` and the
release descriptions. Publication is authorized for all three beta channels.

## Change boundaries

The exact previously tested `data/UVars_k2.tgi` sets only the desired terrain
texture pool size to 1024. Other UVars settings, models, animations, shadow and
general texture pools, simulation rules and native code payloads are unchanged.
The reviewed terrain allocation changes from 8 MiB (Vanilla/Immortals) or
23 MiB (Arcane Wars) to 64 MiB on the test machine. Previous moving-camera
evidence is in [the A/B report](verification-terrain-pool-2026-10-17.md).
Neither improved frame times nor NVIDIA crash causality is established.

The file belongs to `pawpatch-core` for Arcane Wars and `pure-beta-common` for
Vanilla/Immortals. These are full-patch selections; file-only mode does not get
the overlay. The installer owns the file and reconciles it on mod/channel
changes and patch disable. Arcane's separate `data/UVars.tgi` is preserved.

All eight Arcane and four Pure beta helpers are rebuilt with the new patch
identity and `TERRAIN_TEXTURE_POOL`. Before launching a native process, they
require the exact overlay hash:
`BB73F2CDF722024F89B7C57865A9D33FFE855D80637D7F3FCADEAD8089D25FCA`.
The flag is opt-in in both build entry points. Stable helper builds are not
enabled implicitly. No launcher binary, compatibility protocol, Documents
scan or localization/keybind admission rule is changed.

## Validation

- Overlay validation: 10 checks; preparation safety: 8 tests.
- Actual compiled startup guards: 36 checks across all 12 helpers, accepting
  original bytes and rejecting missing/altered files without launching a game.
- Eight Arcane helpers: five offline self-test modes each.
- Four Pure helpers: deterministic rebuilds; 816 guarded composition installs,
  477 random-map transaction checks, 24 channel/mode checks and 12 save-copy
  preservation compositions. AI option checks: 145 for each mod-family profile.
- Installer/preflight matrix: 16 Pure cases, including six languages per mod,
  beta-to-stable and patch-disable cases. The overlay is absent after disabling.

- Native-menu matrix: 12 fresh launches for Vanilla/Immortals (all six text
  languages per mod), plus three fresh Arcane launches with AI on/off/on.
  Two unchanged patch-disabled cases were reused, not counted as new launches.
- Arcane beta-to-stable and patch-disable reconciliation passed; the overlay
  is absent afterward. Its UVars.tgi matched the original mod hash before
  rollback. All 45 unchanged Pure build inputs match the previous release.

Fixtures are under `D:/PawWidescreen-20261013`, use a private Documents profile,
and run one native game at a time. The native-menu oracle ends only its verified
test process; these are not complete match/normal-exit tests. No VM recording.

## Packaging and publication

`tools/PrepareTerrainPoolBeta.py` pins the signed public baseline, emits seven
immutable archives and a payload audit, and signs local test feeds. New payload
files are limited to the overlay in the two full-patch data packages. Changed
payload files are the helper executables and Arcane's version INI; the Pure
base-data package receives a version bump with identical data bytes.

Promotion requires matching validation asset hashes, public prerelease/tag
verification and byte-exact public downloads before signing the beta catalog.
Stable and legacy catalogs, launcher identity and unrelated packages remain
unchanged. Public readback is a separate required step after pushing the feed.
Release evidence is retained in `outputs/publish-terrain-betas-20261018`.

Remaining beta validation: long player sessions on large maps, low-memory
hardware/fallback, custom resource override ordering and multiplayer with
different cosmetic settings. No additional assertion suppression was introduced.
