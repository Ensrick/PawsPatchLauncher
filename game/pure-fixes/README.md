# Paw's Patch for Vanilla and Immortals

## Current release: 0.4.0

`build_beta.py` builds the extended Pure composition for stable 0.4.0,
runtime `1.3.72-pure.14`. `tools/PreparePure040.py` promotes the accepted
0.4.0-beta.2 data byte-for-byte, publishes new immutable package identities,
and verifies public assets before signing both current channel catalogs.
The names `pure-beta-common`, `PureBetaFeatures` and configuration marker
`PB1` are retained for compatibility; availability is determined by the feed.

Surviving lair defenders are part of the core runtime. Current feeds advertise
`pureLairRecoveryBuiltIn`; effective settings always report LR1 and the UI has
no separate switch or optional lair package. Historical pinned beta feeds keep
their original optional LR0/LR1 behavior and the user's saved preference.
AI, independent hostility, roaming frequency and authored roaming companies
remain optional. Both supported channels pass the native installation guard.

No city automation, automatic militia, extra building slots, foundation
distribution or fractional kingdom points are linked. The Pure AI profile
retains its Vanilla/Immortals compatibility adaptation. Arcane packages and
local experimental maps, roads, bridges and reconnection work are unchanged.

## Previous stable release: 0.3.0

`build_options.py` builds stable 0.3.0. It creates local,
verified archives; it never installs, starts the game or publishes releases.
`build_channels.py` records the previous 0.1.1 / 0.2.0-beta.1 workflow and is
not the builder for current releases.

| Channel | Patch / data | Runtime | Native transfer | Optional features |
| --- | --- | --- | --- | --- |
| Stable | 0.3.0 | 1.3.72-pure.11 | R2 | 39 colors, continue with first-desync logs |

The release retains the accepted company badges, Dvorak controls (WASD and arrows,
F allied marker), display-only negative-zero fix, terrain initialization,
and menu version label. The original 16 data files are byte-identical to the
previous pure beta. The release includes the 18 accepted Arcane minimap frame textures
(six races, three texture groups); these remain available in file-only mode.
There is no runtime resolution detection. Language selection remains independent.

R2 uses the existing native transfer protocol, ACKs, retries and bandwidth
checks. Its guarded code is shared with Arcane Wars; no R3 change is included.

The release has four helpers: `k2_paws_pure_fixes_1372.exe`,
`k2_paws_pure_colors_1372.exe`, `k2_paws_pure_sync_1372.exe`, and
`k2_paws_pure_colors_sync_1372.exe`. The launcher selects exactly one from the
mode's two independent switches.

Colors reuse the exact accepted compact 39-color native payload, fixups and
palette from Arcane Wars beta. `pure-player-colors` contains only the palette
and a stock staging menu with the compact color control. It does not import
Arcane Wars staging, kingdoms, city automation, balance or map changes.
Color ownership uses native participant identity, as in Arcane Wars.
Retired shades remain available for saved-game lookup but cannot be selected
or randomly assigned in new games.

`PureSync.cs` installs the shared `PawSyncDiagnostics` wrapper only when selected.
It calls the native writer once per network baseline, rearms on new matches and
save loads, and preserves integer, x87 and SSE state. The reset, failure and
continuation sites are guarded and rolled back on failure. Code is RX and the
mutable signal has a separate RW page. It keeps the match running. It does not repair divergent simulation.
Other builds leave the stock synchronization checks unchanged.

`pure-fixes-data` is executable-independent, priority 900. Runtime is native,
priority 910, and depends on the existing menu-runtime package. Optional colors
are native-dependent, priority 920. Both modes require the exact supported
1.3.72 executable for runtime features; unknown executables use file-only data
and stock k2.exe. Master-off excludes pure packages and disables both switches.
Preferences are separate for each mod and restored when enabled again.

```text
python game/pure-fixes/build_options.py --out <fresh-output> --dotnet <dotnet.exe> --analysis-work <verified-analysis-work> --rwd <stock-Data.rwd>
```

The analysis image is hash-pinned. R2 guards cover 15 regions / 1815 bytes.
All four helpers are built twice and must be byte-identical. The builder
runs pure managed/x86 checks, R2 managed/x86 checks, and optional sync rollback,
ASLR and x86 ABI checks. Package manifests and payload hashes are verified.
`scope.json` and `features.json` record scope and exact binary identity.

UI checks in `PreviewRenderer --pure-options-config=<test-config>` cover both
modes/channels and six languages, master-off and unsupported-EXE controls.
Core tests cover selector, configuration sharing, legacy feeds and mod isolation.
Real game startup evidence is recorded separately from automated checks.

## Historical two-hook baseline

The older `build.py` remains an isolated baseline build without menu or channel
features. The following specification documents that baseline only. The
historical local launcher review used `../menu-labels/build.py`. Its
`pure-fixes-runtime 1.3.72-pure.4` enables the separate menu-label block in
`Program.cs`, while retaining the two fixes below. The standalone baseline build
in this folder still excludes that block. `menu-runtime` is a distinct helper
with no engine fixes, used when the mod is selected and Paw's Patch is off.

This independent x86 .NET Framework helper applies exactly two guarded fixes to
the newly launched, path-verified Kohan II 1.3.72 process:

- The limit formatter receives `0` for an exact IEEE-754 negative-zero display
  value. Resource/capacity state itself is not modified.
- The object terrain-flatten caller initializes descriptor field `+0x1C` to
  `-1.0` after invoking the original constructor. The constructor return value,
  flags, floating-point state and RNG call sequence are preserved.

The stock out-of-sync marker and error-handler call are verified before and
after installation and are never changed. There are no game-rule, menu-version,
palette, diplomacy, map-selection, time-selection, hotkey, assistant or transfer
features. The helper does not read or modify language files; game language does
not depend on enabling these fixes.

`pure-fixes-data` is separate: the fourteen accepted company-badge assets from
`../release-assets` (seven NIF models and seven TGA textures). It contains no TGI,
executable or localization file. Its package is executable-independent.

## Packages and launcher integration

- `pure-fixes-data`, version `1.0.0-pure.1`, priority 900,
  `executableIndependent: true`.
- `pure-fixes-runtime`, version `1.3.72-pure.2`, priority 910,
  `executableIndependent: false`; contains only
  `k2_paws_pure_fixes_1372.exe`.
- Both have `required: false`, no dependencies, and
  `mods: ["vanilla", "immortals"]`.

For a selected Vanilla/Immortals Paw component, install the data package. Add the
runtime only when that mode's executable requirement matches and file-only mode
is not active. With incompatible executables keep the data package and launch
`k2.exe`; the runtime itself also rejects an unknown hash before launching.
With the Paw component disabled neither package is required. Game localization
is a separate choice/package in every combination.

The supported original executable is identified by its exact SHA-256:
`1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45`.
It is Kohan II 1.3.72 / Steam build 25068126; its PE FileVersion resource is
absent, so the helper does not rely on that resource or editable data strings.
Keep this requirement scoped to Vanilla/Immortals rather than inheriting future
Arcane Wars executable support automatically. `mod-games.json` is emitted for
that integration.

Supported commands:

```text
k2_paws_pure_fixes_1372.exe
k2_paws_pure_fixes_1372.exe --game-dir "C:\Path\To\Kohan II"
k2_paws_pure_fixes_1372.exe --features
k2_paws_pure_fixes_1372.exe --preflight "C:\Path\To\Kohan II"
```

Normal startup defaults to the helper's own directory. No attach mode exists.
The tooling commands exit before process enumeration or log writing and do not
launch the game. Runtime errors use a nonzero exit code and
`paws_pure_fixes_1372_status.txt`; there are no helper dialogs or console windows.
The helper stays alive until its game exits, so add its process name to launcher
game-observation checks. A failed partial install rolls both hooks back and
stops only the helper's verified, newly created process. Native process handles
are independently checked against the expected path and exact creation time.

## Build and offline checks

Run `build.py` with a new empty output directory, an installed .NET SDK's
`dotnet.exe`, and a Python dependency directory containing Unicorn:

```text
python build.py --out <new-directory> --dotnet <dotnet.exe> --native-deps <Unicorn-directory>
```

Optional arguments:

- `--original-ui-payload <PawCommonUiPayload.bin>` proves that the 47-byte zero
  handler is identical to the previously validated handler at its baseline
  relocation. None of the old menu payload is included.
- `--game-directory <Kohan II-directory>` performs only the hash preflight of
  that installation. It never launches or attaches to the game.

The build uses Roslyn with deterministic output and Windows .NET Framework
references. It compiles the runtime twice in distinct output directories and
requires byte equality. ZIP manifests/payloads are deterministic and verified
by hash. Existing package archives are never overwritten with different bytes.

`Tests.cs` tests both-hook installation, every injected operation failure,
rollback after partial writes, unknown-signature rejection before allocation,
ASLR, launch identity and unknown executable rejection. A separate inert,
hidden instance of the test executable checks native handle identity and
suspend/read/resume rights. It is not k2.exe and creates no UI.

`test_native.py` executes the exact C#-generated machine code in Unicorn. Tests
cover negative zero, positive zero, ordinary numbers, NaNs/infinities, 1,000
random bit patterns, poisoned terrain fields, register/FP/stack preservation,
write boundaries and calls to only the original formatter/constructor. The
remaining allocated bytes must be zero, proving no additional embedded feature
payload is present.

Build outputs include `packages.json`, `mod-games.json`, `features.json`, all
module manifests and `build-verification.json`. These checks do not replace
visual badge/HUD acceptance or real repeated-match/network validation. The build
does not install into a game, start k2.exe, open native windows, or publish.
