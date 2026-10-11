# Large maps — Arcane Wars beta 0.4.1

Kohan II 1.3.7.2 / Arcane Wars 0.82.1.8. The R10 implementation below is
packaged for the Arcane Wars beta channel as 0.4.1. Stable Arcane Wars and
both Vanilla/Immortals channels retain their existing payloads.

Production builds use `game/beta7/build.ps1 -LargeMaps -PatchVersion 0.4.1`
with the complete Arcane feature switches, `-NativeCompiler`, and a verified
`-GameExecutable`. This rebuilds/tests the native payload and includes it in
all eight helpers. `tools/PrepareLargeMapsBeta.py` verifies the feature delta,
expands both template owners, signs the beta catalog, and checks public bytes
before promotion. `September20Tests --verify-september20` covers the actual
launcher installer, all selections, upgrade/downgrade and localized preflight.

The slow scene cleanup measured on 2026-10-11 is not modified. New worlds use
the extended save format; old-format worlds retain their existing format.
New-format saves require a compatible helper. No server schema or launcher
configuration format changes are needed. Release notes describe player-facing
changes; the following sections preserve the local test history and limits.

## R10 centered minimap markers — 2026-10-11

Use `extended-r10/k2_paws_lobby_colors_mp_sync_1372.exe` with `registry-r7`.
SHA-256: `39389B4E2704C4B007E87AD7C1F382391C3D89A4BC0672EBD60CAECB71547588`.
Includes the previous local fixes; installed data and saves are unchanged.

The stock `MiniMap/Icons/SettlementCenterMiniMapIcon.png` extracted read-only
from Data.rwd is 7x7 with a single black center pixel. R9 reduced it to 4x4 on
2048 maps, selecting columns/rows 0,2,4,6 and losing that center pixel. The
native renderer subtracts floor(size/2), so an even-sized reduced marker also
has its geometric center half a texel left/up of its position. Reduced sizes
now round up to an odd size (bounded by original dimensions). The city marker
becomes 5x5 on 2048 maps, retaining the center dot and bilateral symmetry.

Validation: 1,431 native/emulation checks, including reconstructed stock city
art on eight map sizes, center-pixel and reflection checks and actual native
rasterization; 528 installer checks; five integrated helper self-tests and
installed-data preflight passed. No running match was modified or restarted;
in-game visual acceptance of R10 is pending.

## R9 minimap marker scaling — 2026-10-11

Use `extended-r9/k2_paws_lobby_colors_mp_sync_1372.exe` with `registry-r6`.
SHA-256: `3EB750D8DA94041D524DBB14EA672E358DCF131DED595A544F6406989E6F8824`.
Includes R7/R8. Installed files and save format are unchanged.

Textured minimap markers are resampled before the native raster loop using
`min(1, 1024 / max(worldWidth, worldHeight))`, rounded to whole source pixels.
Each dimension has a three-pixel floor, never exceeding its original size.
Maps up to 1024 retain their original icons. A 9-pixel marker becomes 6 pixels
at 1536 and 5 at 2048. Nearest-center sampling retains hard pixel-art edges.
Native position, visibility/fog, tint, priority, outline and clipping remain
in place. Untextured company dots and the camera outline are unchanged.
The world dimensions are read during drawing, so loading another world does
not retain the previous world's scale. No UI text or localization is added.

The resampled source uses fixed local scratch storage, consumed synchronously
by the native renderer; no textures or game objects are modified. Unknown
textures over 64 pixels or invalid dimensions use the original rendering.

Validation: 1,377 native/emulation checks at three relocated bases, including
resampling of padded and rectangular textures, tiny markers, map-size changes,
register/flags/XMM preservation and the actual native textured raster loop;
528 installer/configuration/rollback checks; five helper self-tests and local
installation preflight passed. The user's running process was not modified.
Visual acceptance in a running R9 game remains pending.

## R8 performance warning — 2026-10-11

Use `extended-r8/k2_paws_lobby_colors_mp_sync_1372.exe` with `registry-r5`.
SHA-256: `9AF4D425799DC76582D76B1FC8B1EA1C5C6B911011BB16ED6A07EEC6E3FF303C`.
Includes R7's terrain hash fix; no additional save-format or template changes.

Starting a random map with both dimensions at least 1536 and applied roaming
frequency x2/x4 displays the native staging Continue/Cancel dialog with a
performance warning. Frequency comes from installed `appliedSettings`, not
unapplied launcher selections. Map dimensions are checked again on every Start
click. Loading saves/replays does not trigger it. The original small-map
player-count warning is retained. The body follows the game's en/ru/de/fr/cs/uk
language; buttons and launch/cancel callbacks remain the game's own.

Validation: 1,185 compiled x86/emulation checks, including all translations,
thresholds, asymmetric maps, NaN, modes, cancellation/recheck and native stack/
register preservation at three relocated bases; 514 installer/configuration/
rollback checks; five integrated helper self-tests. UI services are mocked in
emulation. The R8 dialog has not yet been visually checked in a running game.

## R7 follow-up — 2026-10-11

Use `extended-r7/k2_paws_lobby_colors_mp_sync_1372.exe` with `registry-r4`.
SHA-256: `8187F60D8BAC1F385B508A66CFE38CE54E54B804247841184ACD5165FD89419F`.
The save format and installed template are unchanged from R6.

The user's new 2048 x 2048 Arctic match crashed in `log-440.dmp` at image
RVA 0x6f5f5, called from RVA 0x1a788a. The terrain variant table index was
-1 (EDX=0xffffffff); the fetched pointer ECX=0x00650074 produced an invalid
read at 0x00650090. The caller calculates `(4723*tileIndex+1600)%6997` in
signed 32-bit arithmetic, overflowing above tile index 454686 and allowing
a negative remainder to index before the pointer array. This is independent
of the object registry capacity. Heap data and the exact linear tile index
are absent from the minidump, so exact original coordinates cannot be recovered.

The local patch widens that multiplication/addition to signed 64-bit before
the original remainder division. It does not blanket-ignore invalid pointers.
All results below the overflow threshold remain unchanged. The second use of
constant 4723 at RVA 0x24268c produces a floating variation value rather than
a pointer index; it has not been changed by this targeted crash fix.

R7 validation: 408 native emulation assertions at three image bases (including
reproduction of the old negative index and corrected boundary cases), 437
installer assertions, five integrated helper self-tests. No game was launched
on 2026-10-11 after this fix; in-game confirmation is pending. Crash evidence is
preserved in `../crash-large-maps-20261011` relative to the checkout.

The map template offers additional square sizes 1280, 1408, 1536, 1664,
1792, 1920 and 2048. The extended object registry has 262,143 usable slots:
65,535 primary objects and 196,608 additional generic scenery objects.
This is a finite increase, not an unlimited object count.

## Implementation

`registry.c` preserves the native primary table for actors and derived types.
Generic objects use three extra banks. New-world IDs retain 16 low index bits,
use 14 generation bits and two bank bits. Object-reference serialization uses
18 index bits. Registry serialization marks the extended format explicitly.
Old 65,536-slot saves keep native 16-bit reference/generation behavior.

The installer checks original instructions and requires the world registry to
be uninitialized. It verifies writes and rolls back failed installation. An
uncertain rollback retains its code allocation rather than freeing live code.
No live match is converted. No random decisions, ownership or AI policy are
changed by the registry extension.

Native adaptations include checked lookup, iteration, lifecycle operations,
seven inline index readers, the world-load inline generation check and five
world save/load allocation/free sites. The latter need 262,144 offsets rather
than 65,536. Omitting these caused heap corruption in an earlier local build;
omitting the inline generation check skipped extra-bank scenery on reload.

## Validation

- `test_registry.py`: 366 assertions using actual compiled x86 code at three
  image bases, bank boundaries, stale IDs, old-format serialization, inline
  register/flag preservation and enlarged world offset allocations.
- `InstallerTests.cs`: 423 assertions including failed/partial writes and
  rollback failure.
- Five integrated helper self-tests passed.
- Existing `Случайно.RSG` dated 2026-10-05 loaded in the same process after
  the large world: 4,298 objects, mode 16, no extra-bank objects, errors zero.
  The match displayed normally and resumed. The game subsequently exited
  normally, flushing `log-439-ok.log` (preserved as `r6-runtime-ok.log`).
- The 2048 x 2048 game generated approximately 94,000 registered objects.
- R6 loaded the R5 save with 89,359 generic objects restored; recruitment and
  movement through scenery worked. A fresh R6 save was written and reloaded
  in the same process. After reloading: 94,539 total objects, 89,352 extra-bank
  objects, registry error counter zero. Scenery and the recruited company
  were visible at their saved positions.
- The in-match reload took minutes and temporarily stopped responding, then
  completed. This test does not establish fast transitions or long-session
  stability. Multiplayer has not been tested.
- The earlier R5 save produced two native projectile warnings about a NULL
  ability definition. The loader reads that ability by name; its cause has
  not been established. Do not describe this build as warning-free.
  No further NULL-ability warnings occurred on the subsequent R6 reload.
  The corrupt R4 save from the earlier failed experiment was moved out of
  the game's Save folder into the artifact folder for preservation.

## Local artifacts

Artifacts live in `../large-maps-20261009` relative to the checkout:

- `extended-r6/k2_paws_lobby_colors_mp_sync_1372.exe`
- helper SHA-256: `958D81F702846488E44B5C381E22EC76636D64F30C628D1918EF986438B4C876`
- compiled registry and guard manifest: `registry-r3`
- full baseline payload: `build-r2`
- original map template: `data/template.original.tgi`

Launch the R6 helper directly. It locates the local Steam installation (or
accepts `--game-dir`). The expanded template is already installed there.
Stock/release helpers reject that template; restore the original template
before returning to normal launcher play. Keep extended-world saves separate:
they require this experimental helper and are not stock-compatible.

New worlds use a 14-bit generation counter, wrapping after 16,384 reuses of
the same slot. This and complete large-world multiplayer/save compatibility
require further multiplayer/long-session testing before stable promotion. The existing lobby compatibility
signature includes the helper file hash, so the experimental helper differs
from the release helper even though the displayed patch version stays 0.4.0.
