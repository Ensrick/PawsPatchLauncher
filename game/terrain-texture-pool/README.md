# Terrain texture pool for gameplay zoom 2.0

Original proposal and overlay by Ensrick, [PR #16](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/16),
head `8ee2097752c88034d6307071d37c9bfe43d2f0da`. MIT attribution is retained in
`LICENSE.Ensrick`. The initial local experiment is integrated into the
0.4.2-beta.3 Arcane Wars and 0.4.1-beta.5 Vanilla/Immortals release packages.
It is not an established crash fix.

Data-only overlay: `data/UVars_k2.tgi` is the retail 1.3.72 file plus one line,
`int TerrainTexturePoolDesiredNumEntries = 1024`. Retail `UVars.tgi` sets 120 with the comment
"Approximately 20% more than on-screen for max-zoom", sized for the stock gameplay camera
(zoom 1.21, far plane 512). CAMERA_ZOOM r1 allows 2.0 and 1024, which puts up to roughly
(2.0 / 1.21)^2 = 2.7 times more ground on screen, before the wider view of 16:9 and 21:9 displays.

`prepare.py --game <installed root> --out <new directory>` reads `UVars_k2.tgi` from `Data.rwd`,
requires the retail bytes and writes the overlay and a before/after SHA-256 manifest. The checked-in
`data` directory is that output. `validate.py` checks it without the game.

## Native evidence (1.3.72, RVAs)

- 18B2E8: `KKC_Renderer::CreateTexturePools` reads `TerrainTexturePoolDesiredNumEntries` by name (call at
  18B2ED) together with the resolution, minimum resolution, alpha and mipmap variables.
- 185BD2: the pool keeps that count at +0x14. 185CB9 creates every entry up front (loop 185D63..185DB0)
  at the desired resolution (128).
- 18B462 (`KKC_Renderer::AllocTexturePools`): if creation fails, 186004 halves the texture resolution down to the minimum (64) and retries,
  so a card without room for the larger pool gets smaller terrain textures instead of an error.
- 185F09: when no free entry is left, the pool takes the oldest entry in use and 185970 copies it to system
  memory (log line `Offloading Rendered Texture %x to System Memory`) before reusing it.

## Why a data file and not a hook

The game loads AVars.tgi, AVars_K2.tgi, AVars_Locale.tgi, AVars_version.tgi, UVars.tgi, UVars_k2.tgi and
User/UVars.tgi, then creates its window and right after it the texture pools (about 0.4 s after start, in
the game log). The helpers attach after the window appears, so a hook at 18B2ED would miss the only pool
creation. `User/UVars.tgi` does not work either: tested, the variable there is ignored and the game drops
the line when it rewrites the file on exit. A loose `data/UVars_k2.tgi` is read at startup on every launch
path. Immortals 2.1 and Arcane Wars 0.82.1.8 have no `UVars_k2.tgi`; see the local package audit below.

## Upstream measurements (Ensrick)

Texture memory, from the game log (`Allocated TexturePool Terrain (mem ...)`, difference to `Allocating`):
120 entries 8,388,608 bytes, 1024 entries 67,108,864 bytes, allocated once at startup.

Two real single-player 1.3.72 matches with Paw's Patch 0.4.0 (stable, Immortals 2.1, 3840x2160,
about 16 minutes each) logged 5,404 and 4,301 offloads, up to about 12 a second. The first ended in an
access violation in d3d9 `CMipMap::Release` called from the `NiDX9SourceTextureData` destructor, and its
shutdown logged `resource Dynamic/Terrain x35 y33 OVER-RELEASED (count -1)`. Whether the offload path
causes that release is not proven.

Test copy, one windowed instance (2560x1440, parked off-screen) loading a 1.3.72 Immortals save, with
CAMERA_ZOOM's checks and values applied by a test-only stand-in and the gameplay camera held at 2.0:

| Overlay | Terrain pool memory | Offloads while loading | Offloads, 60 s still at zoom 2.0 | Render fps |
| --- | --- | --- | --- | --- |
| none (retail 120) | 8 MB | 181 | 0 | 95-111 |
| `prepare.py` output in Documents data | 64 MB | 0 | 0 | 95-110 |
| `prepare.py` output in the game's `data/` | 64 MB | 0 | 0 | 95-110 |

A larger late-game save: retail 80 offloads while loading, 0 still. In real matches the offloads come
from moving the camera (the pool keeps recently drawn terrain), which could not be scripted: the game
reads keyboard and mouse directly, and neither console commands nor window messages moved its camera.

## Remaining validation

- Long player sessions, large-map stress and low-memory hardware. The local scripted camera
  comparison below is shorter and uses a 384 x 384 Vanilla save.
- Whether the retail connection data check covers `UVars_k2.tgi`; every player gets the same file from the
same package.
- A card that needs the resolution fallback; its existence is not proof that every allocation failure is safe.

## Integration

The beta packages install `data/UVars_k2.tgi` in Arcane's `pawpatch-core` and
Pure's `pure-beta-common`, which follow the full patch/camera selection rather
than file-only mode. Helpers built with `TERRAIN_TEXTURE_POOL` validate its
exact hash before launching. The reviewed native consumer is the renderer;
network file-check behavior still needs validation. See
[release verification](../../docs/verification-terrain-betas-2026-10-18.md).

## Local integration audit (2026-10-17)

The official Immortals 2.1.0 package has neither UVars file; it inherits retail
120. Arcane Wars 0.82.1.8-clean.1 supplies `data/UVars.tgi` with 360 terrain
entries, but no `UVars_k2.tgi`. The latter retail layer therefore remains the
base for this overlay in all three mods. Do not replace Arcane Wars' UVars.tgi:
its other renderer, texture-pool and camera settings are independent of this
change. Package hashes and live-run evidence are recorded in
[the verification report](../../docs/verification-terrain-pool-2026-10-17.md).

The local Vanilla comparison loads the same save twice, applies the unchanged
production camera patch, reads back zoom 2.0 / far plane 1024, and executes the
same 12 camera positions. Terrain offloads fell from 1,790 to zero, while the
reported terrain allocation rose from 8 to 64 MiB. Both runs exited normally.
This establishes reduced offloading in this scenario, not an FPS improvement
or a fix for the reported graphics-driver crashes.

Preparation rejects an unknown loose `data/UVars_k2.tgi`, unsupported retail
bytes and an existing output directory. It only writes a NEW output directory;
it does not install or edit the game. It cannot discover effective overrides
inside arbitrary custom RWDs or Documents depots. Such configurations require
separate load-order review before use.

Run `python validate.py` and `python test_prepare.py` for byte-preservation and
preparation/parser safety checks. These do not establish runtime performance.
`StartupGuardTests.cs` exercises the actual guard inside built helpers against
valid, missing and altered files. Multiplayer with differing localizations and
keybinds remains a separate validation gate; this change adds no new cosmetic
compatibility restrictions.
