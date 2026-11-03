# Integrated widescreen UI (Kohan II 1.3.72)

Ensrick identified the stock `WidescreenUniformUI` setting (PR #14). The beta
helpers enable its aspect calculation inside Paw's Patch while the current
application-state object has the verified `KKC_GameAppState` vtable, without
changing the user's saved UVars or adding a launcher switch.
Menus, lobby and startup movies use the stock coordinate system. The native
setting-change poll recalculates the canvas on screen transitions. During a match
the UI width is at least 1024
logical units, so 4:3 and narrower screens retain the original widget grid.
World rendering, simulation, keybinds and localization are unchanged.

`GamePresentation1372.cs` guards all three native sites before writing: the
aspect branch, a minimum-width clamp, and the stock main-thread setting-change
poll. The latter handles attachment after the first resolution calculation.
Failures restore attempted patches; no generic assertion skipping is involved.
Core-less Arcane Wars helpers do not enable the feature.

The stock background would stretch even with uniform UI enabled. A fixed-width
panel was rejected during visual QA because it left the entire HUD on the left.
The earlier beta used `DecalVETiledHorizontal`. Both it and the first ordinary
decal replacement swallowed clicks above the visible panel. Native picking
checks client rectangles, not texture alpha. All three decorative decals now
explicitly use `client_T = 183b`, preserving the stock panel client band while
their full artwork remains visible. The local replacement uses three ordinary
`DecalVE` elements:

- Lossless crops of the original atlas halves anchor the minimap left and
  commands right, retaining the original pixels and transparency.
- A 128-logical-pixel decorative segment fills the extra width without sampling
  the right-panel border. All three source texture sizes are cropped losslessly.
- Side-panel tabs and action buttons anchor to the right; the object display
  fills the middle. Its portraits and multi-selection cards remain beside the
  minimap. The single-company banner, morale/efficiency bars and both formation
  panels anchor to the right edge of this input container. At width 1024 their
  original coordinates remain unchanged.
- All six racial skins and the observer skin have all three stock atlas sizes.
- Arcane Wars retains its original compact company-member spacing; that mod's
  layout is separately pinned; company-member spacing is not changed.

The 21 former loose minimap-frame filler textures are retired only in these
beta packages. Stock backgrounds remain in their original RWD archives.

Build the 67 resource overrides from owned stock archives (no game launch):

```powershell
dotnet run --project game/widescreen-ui/BuildAssets -c Release -- STOCK_GAME game/widescreen-ui/source-assets.json NEW_OUTPUT ARCANE_ORIGINAL_OBJECT_DISPLAY
```

The builder checks every source SHA-256, TGA encoding, crop row and layout
anchor. `source-assets.json` pins the supported retail resources. Outputs are
four layouts (including the centered timer above the HUD), 21 TGA strips,
42 atlas halves and a hash manifest per mod family.
`test_panel_alpha.py ASSET_OUTPUT` verifies transparent world/button positions
and opaque panel positions across all skins and texture variants. It does not
substitute for live picking/selection acceptance.
`test_panel_picking.py --legacy RESEARCH_ROOT --assets ASSET_OUTPUT --out REPORT`
executes original GroupVE and VE picking against a minimal widget tree at
three image bases and ten resolutions. It reproduces the old obstruction,
then verifies field/button routing and preservation of minimap, pause and
opaque-panel hits using client bounds parsed from the generated layouts.
The equivalent three-rectangle change in the user's running test match was
manually accepted on 2026-10-20; restart acceptance of the persisted asset
remains separate. The beta staging
tool checks this manifest, preserves unrelated payload bytes, and requires
native and installation/rollback evidence before feed promotion. Live acceptance
is recorded separately and must not be inferred from automated checks.

`test_native.py --legacy RESEARCH_ROOT` emulates actual stock executable bytes
at three ASLR bases, nine dimensions and both saved flag values. Native
presentation composition tests exercise the guarded patches and rollback.

Revision 2 also uses `button.tgi` for the beta Paw's Patch button. Its square
56-unit frame sits six units above the visible lower bar; the tooltip
follows the logical right edge. `prepare_button.py` updates only this widget.
`test_phase_button.py` emulates the emitted C# app-state and tooltip payloads.
The engine run phase at RVA `0x5f9218` does not distinguish menus from matches:
the first local candidate used it incorrectly and failed manual acceptance.
The current candidate bounds the index at `0x5f92f4`, reads the native state
table at `0x5f92b4`, and compares the state object's vtable with RVA `0x48d0dc`.
The native test verifies that vtable's RTTI as `KKC_GameAppState`.
Pass `--emitted PRESENTATION_TEST_FIXTURE` to `test_native.py` so it tests the
actual C# payload rather than a duplicated implementation.
Legacy builders retain the original gold-button asset.

`prepare_local_test.py` creates an isolated Arcane Wars copy and private user
settings for manual acceptance. Compile `ManualTestLauncher.cs` into that
fixture; preparation does not launch the game. Keep visual checks of menus,
movies, match transitions and button clicks separate from the automated tests.

Revision 3 (October 27) adds guarded native presentation hooks generated
by `build_hud_native.py`. `--update-source game/beta7/GamePresentation1372.cs`
updates its marked C# block. The bounds hook adjusts only named primary views
inside a match (recruitment editor, favorites, connection dialog and economy
preview). The ordinary tooltip wrapper temporarily uses the actual logical
right edge and restores the interface afterwards.

At RVA `0xdad14`, the group-layout hook replays the original subtraction and caps
the available six-card area at 412 logical units before native division and
rounding. Parent bounds remain expanded, so the right-anchored formation buttons
are inside both rendering and picking rectangles. This fixes card stretching
without moving building/portrait content or changing orders and formation logic.
`test_selection_layout.py --assets ASSETS --emitted EMITTED --legacy RESEARCH_ROOT`
checks both generated mod layouts, the original guard/divisor, emitted machine
code, preserved registers/stack and 1–6 card layouts across seven resolutions.
Actual single/multi-selection visuals and clicking are separate manual checks.
The user authorized beta publication of revision 3; the exact final right-anchor
build has not been claimed as manually accepted. See the release verification
report for the checks performed on the published packages.

## Square icon layouts (October 29, local candidate)

Uniform HUD scaling exposes several originally non-square icon canvases. The
icon pass corrects individual rectangles instead of adding another global scale:
hero portraits and their health-bar cells, faction abilities, recruitment
candidates and selected slots, favorite-template portraits and role symbols,
formation buttons, unit-stat icons, building upgrades and unit portraits.
The full company banners remain vertical because they include morale and troop
status. Existing square action buttons and the revision 3 panel anchors remain
unchanged. This pass changes no executable code or gameplay values.

Build revision 3 first, then add the icon layouts:

```powershell
python game/widescreen-ui/build_icon_assets.py --stock STOCK_UI_GAME --arcane ORIGINAL_ARCANE_UI_GAME --base-assets REVISION_3_ASSETS --out NEW_ICON_OUTPUT
python game/widescreen-ui/test_icon_aspect.py --assets NEW_ICON_OUTPUT --report ICON_REPORT.json
```

Both input directories contain the original `*.tgi` layouts, not previously
transformed files. `icon-sources.json` pins their SHA-256 values. The builder
validates both families before writing output, retains every one of the 67
revision 3 assets byte-for-byte, and adds four layouts per family (71 total).
Arcane-specific powers visibility and compact spacing are retained; text and
all non-geometric properties are checked for equality. This is an additional
build step; old beta preparation scripts that require exactly 67 assets must
not be used unchanged to package this candidate.

`test_icon_aspect.py` resolves parent-relative insets, pressed-client rectangles,
health/recharge strips and formation-panel capacity. It covers both families
and seven resolutions, including 4:3, 16:10, 16:9, ultrawide and 4K. Keep these
geometry checks separate from actual rendering and click acceptance.

The isolated Arcane Wars fixture was checked at 1280x720 and 1920x1080: hero
selection, formation changes and recruitment captain selection responded to
clicks; live widget bounds confirmed square hero and favorite icons. Vanilla
and Immortals share the pure layout family, which received automated checks,
not a separate live match in this pass. No publication is included in this work.
