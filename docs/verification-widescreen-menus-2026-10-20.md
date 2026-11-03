# Widescreen menus and Paw's Patch button — revised local candidate

User requested manual testing themselves. No game was launched or controlled.
No release, feed, installed game or original user-settings files were modified.

## Manual feedback and correction

The first candidate failed the user's screenshot review: lobby, server list,
loading and scoring remained aligned to the left. The button was square and
showed its tooltip, but was too high. Click success was not reported.
The initial predicate incorrectly treated engine run phase 2 as a match-screen
identifier. That field also remains 2 outside a match.

The replacement reads the bounded application-state table and compares its
current object's vtable to KKC_GameAppState. The verified snapshot's RTTI
identifies RVA 0x48d0dc as that type. No guessed screen-number mapping is used.

## Changes

- Scope the effective uniform-UI flag to KKC_GameAppState.
  Other application-state types use the stock canvas. The existing main-thread poll
  detects transitions and recalculates/invalidate the interface.
- Preserve saved UVars values and the existing 1024-unit minimum match width.
- Place a 56 × 56 logical-unit beta button close to the lower control panel:
  right edge minus 60..4, vertical range 514..570; six-unit gap to the visible
  bar at 576. Its tooltip anchor moves down to 510 as well.
- Anchor the tooltip to the actual logical right edge and above the button.
- Preserve existing legacy assets and runtime composition when uniform UI is
  disabled or the revision-2 button marker is absent.

## Offline evidence

- Uniform presentation composition: 1013 checks passed.
- Legacy presentation composition: 605 checks passed.
- Native aspect/poll emulation: 2448 checks passed.
- Actual emitted app-state/tooltip code and button geometry: 738 checks passed,
  three relocation bases and ten sizes. Includes null state objects, all 16
  table slots, invalid indexes and a constant engine run phase of 2.
- Existing native button input regression: 481 checks passed.
- Native hover selector: 276 checks passed.
- Eight Arcane Wars helpers built and passed their offline self-tests.
- Four shared Vanilla/Immortals helpers built; feature comparison against the
  published terrain beta changes only widescreenUniformUiRevision 1 → 2.
- All fixture manifest hashes verified; copied native k2.exe matches source.
- git diff --check passed (line-ending warnings only).

Artifacts: task workspace `outputs/widescreen-menus-20261020`.
Shared helpers for this candidate: `pure-r3` in that output directory.
Manual fixture: `D:\PawWidescreenMenusTest`.
Entry point: `D:\PawWidescreenMenusTest\Проверка интерфейса.exe`.
Entry-point SHA-256:
`745369D98771C83914E7C62DE94CDBFB1FC64137E7252D158D7A78BDC167EE74`.
Selected helper: `k2_paws_lobby_colors_mp_sync_1372.exe`.
Selected-helper SHA-256:
`75f1cc808db39d980116b9483189f195054819970494f5d54e048a1e2a6a24c0`.
The rejected helper/layout files are preserved in the fixture's
`previous-candidate-20261020-1` folder, outside the game directory.

## Pending manual acceptance

With Steam running and the other game closed, start the fixture entry point.
Check startup movie, main menu, lobby, settings and return from match to lobby.
In a match check HUD anchors, square Paw's Patch button, tooltip and click sound.
These live outcomes are unverified. Do not promote from offline evidence alone.

## Follow-up: lower-panel input obstruction

The next manual test reported that the transparent band above the bottom bar
blocked both battlefield selection and the Paw button. Moving the button down
had exposed the unresolved input obstruction. The original texture renderer
had been replaced by DecalVETiledHorizontal in the first widescreen beta.

The new local asset candidate replaces that tiled-directional background with
three ordinary DecalVE children. Two lossless atlas halves retain the original
left/right artwork and alpha; a narrow repeated centre extends the middle.
The enclosing GroupVE explicitly permits multiple decals with identical when
flags. The button remains at 514..570; no new helper or simulation changes.

- Asset builder: 5483 checks passed, 66 assets per mod family.
- Alpha regression: 6532 checks passed; seven skins, three texture variants,
  ten display sizes, clear world/button samples and opaque lower-panel samples.
- One layout replaced and 42 texture halves added to the local fixture.
  All output hashes verified; existing centre strips and other layouts match.
- Fixture was closed before installation. Previous layout/manifest and the
  added-file list are in `previous-candidate-before-hitmask`.
- Assets: `outputs/widescreen-menus-20261020/ui-assets-hitmask`.
- Main installed game and published packages remain unchanged.

Native live picking is not proven by these alpha checks. User must verify
unit/building selection throughout the previously blocked band, Paw button
clicks, minimap/buttons and no world click-through through the visible panel.
The historical release staging tool's asset set now accepts all 66 outputs;
its pinned versions and runtime gates still need the normal new-release update.

## Confirmed cause and live input acceptance

The ordinary-decal candidate also failed manual acceptance. Its alpha tests
proved appearance only; the earlier texture-based hit-mask explanation was
incorrect. With the user's permission, read-only inspection of the running
test game identified the actual native picking path:

- `TGC_GroupVE` pick at RVA `0x2ad0e0` traverses children in reverse order.
- The base picker at RVA `0x2ac629` checks enabled flags and the rectangular
  client bounds at VE +0x54. It does not consult the texture's alpha channel.
- At 2560x1440, all three decorative panel decals had client bounds starting
  at y=960, while the stock panel client area started at y=1096.875.
- As a result, the panel intercepted both world and Paw button input in the
  transparent band. The full VWorld and button bounds themselves were correct.

Executed the real picking routine in a local emulator using lazily copied
pages from the running process (no remote function execution). Across 28
screen positions it reproduced ControlPanel intercepting the lower field
and button. Adjusting just the three decal client-top values in that copy
restored VWorld/PawGoldSoundButton hits and retained lower-panel hits.
Evidence: `native-picking-client-rect-test.json` in the task output directory.

Applied the equivalent change to exactly 12 bytes of live UI geometry in test
process 31612. Original bytes are recorded in `live-client-rect-backup.json`.
No executable code or simulation state was changed. User tested selection,
selection rectangle and Paw button and replied **"Всё работает"**.

Persisted fix: each of the three decorative decals now has `client_T = 183b`.
Visual bounds and texture bytes remain unchanged; minimap and buttons retain
their independent picking views. Outputs are `ui-assets-client-rect` for
Arcane Wars and the shared Vanilla/Immortals family.

- Asset builder: 5483 checks passed.
- Alpha regression: 6532 checks passed.
- New native picking regression: 2344 checks passed, two families, ten
  resolutions and three relocation bases. Actual native code, synthetic
  widget trees, no substituted hit-test routines. Generated client bounds
  are parsed from the output layouts; the old failure is reproduced first.
- Replaced only `data/ui/game/control_panel.tgi` in the isolated test fixture;
  all 77 fixture manifest file hashes verified. Previous layout and manifest
  are preserved in `previous-candidate-before-client-rect` outside the game.
- The open game was neither restarted nor controlled by the agent.

Live input is now manually accepted. Reloading the persisted asset after a
restart, menu/movie layout and other aspect ratios still require their own
manual acceptance. No publication or main-install modification performed.
