# Terrain texture pool: local investigation, 2026-10-17

Status: experimental local integration of Ensrick's [PR #16](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/16),
head `8ee2097752c88034d6307071d37c9bfe43d2f0da`. No release, catalog, installer,
startup hash list or multiplayer compatibility policy was changed. MIT attribution
is preserved beside the module. This is not a confirmed fix for NVIDIA crashes.

## Implemented scope

`game/terrain-texture-pool` adds one renderer setting to the exact retail
`UVars_k2.tgi`: `TerrainTexturePoolDesiredNumEntries = 1024`. The preparation
tool writes a new directory outside the game. It rejects unknown retail bytes,
an unknown loose override, duplicate selected archive entries, an oversized
configuration, invalid section bounds and existing output. It leaves the mod's
separate `UVars.tgi` layer untouched. Arbitrary custom RWD/Document overrides
are outside this audit; this tool does not scan or modify Documents.

Stock `UVars_k2.tgi` SHA-256:
`8e6257c30cfcefcc0661917aa0d5c5966281bea9e5741baa582844d839d700ed`.
Overlay SHA-256:
`bb73f2cdf722024f89b7c57865a9d33ffe855d80637d7f3fcadead8089d25fca`.
Deleting exactly the added line recovers all original bytes and CRLF endings.

## Package audit

| Base | Original desired entries | UVars_k2 supplied by mod |
| --- | ---: | --- |
| Vanilla 1.3.72 | 120 | Retail base |
| Immortals 2.1.0 | 120 inherited | No |
| Arcane Wars 0.82.1.8-clean.1 | 360 in UVars.tgi | No |

Verified package SHA-256:

- Immortals: `6c322c94020b449d681764aaf3c8db5a7b5cd53682b0b5a96cd1fbbe46cb4769`.
- Arcane Wars: `d187e0de5920f591cf8668616cc036dce6d261ecc436c68ebc657c565e2a049c`.
- Arcane Wars' preserved `data/UVars.tgi`:
  `41e162819f36eabaa32701be70d170da45556bd044b77e983efc4cc0e523013f`.

## Moving-camera comparison

Two sequential single-player Vanilla runs, one game process at a time, isolated
game copies and private Documents folders, windowed 1920 x 1080. The same
384 x 384 Midlands save was loaded in each run. Save SHA-256:
`91eaf61fe9d8b9a84176a08c34422107fb504cb0d81454aec3eb7b771ad95201`.

The test helper applies the unchanged production `CameraZoomPatch` after the
game window appears. It validates the executable, process creation time and
original bytes. Memory samples confirm zoom **2.0**, far plane **1024** for
54 samples in each run. Native `DeltaZoom -1` twice reaches maximum gameplay
zoom; `LookAt` moves between (64,64), (192,64), (192,192), (64,192), repeated
three times with four-second intervals. The QA plugin dispatches commands on
the game thread. `UserReady` first dismisses the single-player Continue screen.

| Measurement | Original pool | 1024-entry pool |
| --- | ---: | ---: |
| Terrain allocation reported by log | 8,388,608 B (8 MiB) | 67,108,864 B (64 MiB) |
| Offloads during 12 camera movements | 1,790 | 0 |
| Total terrain offloads | 1,790 | 0 |
| Peak process private bytes at zoom 2 | 282,845,184 B | 266,297,344 B |
| Peak working set at zoom 2 | 292,401,152 B | 276,836,352 B |
| Normal exit and sweep completed | Yes | Yes |

Runs retained under:

- `D:\PawsTerrainPoolTest\runs\20261017-152748-vanilla-baseline`
- `D:\PawsTerrainPoolTest\runs\20261017-153029-vanilla-pool`

Both logs contain one stock `Unknown command: TriggerListUpdate` during the
saved-game UI transition. It is the only explicitly allowed unknown command;
neither log contains the monitored assertion/unhandled-exception markers.
This is a bounded log check, not proof that every possible error was absent.

Earlier setup attempts are retained separately and excluded from this table:
some dispatched commands before entering gameplay or hit the Continue screen;
others used a cinematic camera command clamped to 1.21. One setup attempt
raised a native exception. None is counted as a successful A/B run. The final
controller requires a normal-exit log, all 12 LookAt commands, a sweep-complete
marker and at least 40 camera samples at 2.0 / 1024 before recording success.

## Reproduction and checks

Local controller, camera probe source/binary, generated production payload,
package audit, logs and JSON summaries are retained in
`outputs/terrain-pool-20261017` under the workspace. Run `Run-Local.ps1` with
`-Variant baseline` and then `-Variant pool` to repeat the Vanilla comparison.
It requires the prepared `D:\PawsTerrainPoolTest` fixture, Steam running,
no existing game process, at least 4 GiB available RAM and 6 GiB commit headroom.
Do not run both variants together. Temporary overlays and QA helpers are
removed in cleanup; player saves and the installed game are not test targets.

Retained test-artifact SHA-256:

- `Run-Local.ps1`: `aee0403937a0711e8d5902e38fcc94b712a55b2080546d34648ed16f96626d2f`.
- `CameraProbe.cs`: `495d7f832cd795e3bcd3860d94b3b1679208c59400b6e970d3e16d45f3844be8`.
- `CameraProbe.exe`: `90ae2567733def458d36ad4468bc87d9e0c5846cd72beba3c8216af166f3eecb`.

Validation run locally:

- Overlay/hash checks: **10 passed**.
- Preparation and archive safety tests: **8 passed**.
- Unchanged production camera transaction suite: **138 passed**.
- Four mod startup/normal-exit checks passed (table below).

| Menu-only run | Original pool allocation | Overlay allocation | Outcome |
| --- | ---: | ---: | --- |
| Immortals 2.1.0 | 8 MiB | 64 MiB | Both reached menu marker and exited normally |
| Arcane Wars 0.82.1.8 | 23 MiB | 64 MiB | Both reached menu marker and exited normally |

These four launches did not enter a match and do not establish mod gameplay
performance. Their logs contain no monitored assertion, exception or unknown
command markers. Evidence directories:

- `D:\PawsTerrainPoolTest-immortals\runs\20261017-153533-immortals-baseline`
- `D:\PawsTerrainPoolTest-immortals\runs\20261017-153635-immortals-pool`
- `D:\PawsTerrainPoolTest-arcane-wars\runs\20261017-153731-arcane-wars-baseline`
- `D:\PawsTerrainPoolTest-arcane-wars\runs\20261017-153833-arcane-wars-pool`

After all six accepted runs, verified that no k2 process remained, all temporary
overlays/QA helpers were removed, and Arcane Wars' UVars.tgi still matched its
original package hash. No VM or recording was used.

## Remaining gates

This establishes a reduction in terrain offloading for this short Vanilla
scenario. It does not measure frame-time improvement: native FPS summaries
include loading/menu time and are unsuitable for that claim. The memory
numbers are one-run peaks, not a general memory-saving guarantee.

Before production packaging: test large maps and long matches, lower-memory
hardware and texture-resolution fallback, full-patch combinations, custom-mod
load order and multiplayer connection/state behavior. Preserve locale, voice
and keybind compatibility; do not add cosmetic files to a gameplay mismatch
gate without evidence. Test install/removal and startup hash verification with
the camera feature. NVIDIA driver crash causality remains unconfirmed.
