# Integrated widescreen UI (Kohan II 1.3.72)

Ensrick identified the stock `WidescreenUniformUI` setting (PR #14). The beta
helpers enable its aspect calculation inside Paw's Patch, without changing the
user's saved UVars or adding a launcher switch. The UI width is at least 1024
logical units, so 4:3 and narrower screens retain the original widget grid.
World rendering, simulation, keybinds and localization are unchanged.

`GamePresentation1372.cs` guards all three native sites before writing: the
aspect branch, a minimum-width clamp, and the stock main-thread setting-change
poll. The latter handles attachment after the first resolution calculation.
Failures restore attempted patches; no generic assertion skipping is involved.
Core-less Arcane Wars helpers do not enable the feature.

The stock background would stretch even with uniform UI enabled. A fixed-width
panel was rejected during visual QA because it left the entire HUD on the left.
The shipped layout instead uses the engine's `DecalVETiledHorizontal`:

- Original background atlas halves anchor the minimap left and commands right.
- A two-column lossless crop of the original centre fills the extra width.
- Side-panel tabs and action buttons anchor to the right; the object display
  fills the middle. At width 1024 all original coordinates remain unchanged.
- All six racial skins and the observer skin have all three stock atlas sizes.

The 21 former loose minimap-frame filler textures are retired only in these
beta packages. Stock backgrounds remain in their original RWD archives.

Build the 24 resource overrides from owned stock archives (no game launch):

```powershell
dotnet run --project game/widescreen-ui/BuildAssets -c Release -- STOCK_GAME game/widescreen-ui/source-assets.json NEW_OUTPUT
```

The builder checks every source SHA-256, TGA encoding, crop row and layout
anchor. `source-assets.json` pins the supported retail resources. Outputs are
three UTF-16 layouts, 21 small TGA strips and a hash manifest. The beta staging
tool checks this manifest, preserves unrelated payload bytes, and requires
live UI, native and installation/rollback evidence before feed promotion.

`test_native.py --legacy RESEARCH_ROOT` emulates actual stock executable bytes
at three ASLR bases, nine dimensions and both saved flag values. Native
presentation composition tests exercise the guarded patches and rollback.
