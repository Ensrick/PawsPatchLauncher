# Release 0.2.0 game startup helpers (Beta 7 lineage)

These are the patch-owned C# sources and native resources used for the quiet startup package. The original Kohan II EXE is not included or modified on disk. The supported game is 1.3.72, Steam build 25068126.

Build with Windows .NET Framework 4:

```powershell
./build.ps1 -OutputDirectory C:/PatchBuild/beta7
```

The output directory must be new. The script compiles eight x86 WinExe variants and runs their offline tests; it does not launch or install the game. Install through a matching signed 0.2.0 feed so map, UI, version and palette files are present. The historical directory name is retained for source continuity.

- All eight variants install the accepted terrain initializer and random-map selector.
- The mandatory common-ui package provides all four non-color startup variants.
- The optional player-colors package provides four variants: hostility ON/OFF crossed with official-handling/bypass. `SYNC_ONLY;PAW_COLORS` excludes family-hostility hooks without disabling colors.
- `--features` reports the compile-time switches without starting the game; package tests check them against the actual selected settings.
- Bypass remains an explicit setting and is not a substitute for synchronization.
- Runtime installation is restricted to a freshly launched, path-verified game process. A failed partial startup stops only that verified new process.
- Normal startup creates no helper window, console or child helper. Actual startup errors still receive an error message.

The color payload is the user-accepted r20 payload:
`CC23162A1313E5AC7FBAC8F1B2D13092CCADA550B4AB73F4FBD846688C9BDA6D`.
The random-map payload remains
`FC8AAE172AE11AE4E0C31CF4E3E27527738444737E1EAA7926086B51B1E6D46D`.

Native self-test flags and mock-memory checks are distinct from multiplayer acceptance. See the release validation record.

For a local test helper kept outside the game directory, add `-LairWoundedTest`
to the complete build options. Despite its historical name, this also enables
external game-directory handling for UI layouts, translations and player colors.
Without it, packaged helpers expect these assets beside the EXE. Before sharing
an external helper, run both `--local-data-check <game-directory>` and
`--preflight <game-directory>` on the actual generated executable. A release
helper's ordinary preflight does not verify external UI asset resolution.

## City-assistant Beta build

For patch 0.3.0-beta.3, use `./build.ps1 -CityAssistant -OutputDirectory C:/PatchBuild/city-beta3 -LegacyWorkDirectory C:/VerifiedKohanWork` with a new output directory. The verified work directory supplies the accepted native bridge and transfer sources; the script also recognizes the existing local migration path. This compile-time option embeds city policy r14 in all eight variants and emits both native F1 layouts. Building without it retains Release behavior. `--features` reports `cityPolicyRevision:14`, `nativeCityQueue`, `automaticMines` and `newCityMilitia` only with the assistant; package metadata supplies the public Beta version.

See [assistant sources, controls and validation](../city-assistant/README.md). Do not replace Stable packages with these Beta binaries.

## Saved ownership (released in 0.3.0-beta.8)

The serialized-owner store at RVA 22C7CC always retains the exact owner resolved
from the save, including old independent kingdoms and multiplayer host snapshots.
The materialization hook also skips family reassignment while native SessionSource
kind is 2 (loaded game), except native playback state 4. Films use that same
source kind but must repeat fresh-map family assignment; treating them as saves
changes independent owners, diplomacy and denizen deployment, causing desync.
Real saves still preserve ownership, including later spawns and upgrades.
Starting a fresh match restores the existing family assignment policy. No saved
file is rewritten, and already misassigned ownership in a later save is not
silently reversed. Test with `test_saved_owners.py` against all built helpers;
the test executes the emitted x86 stubs at three relocated image bases, including
film playback, serialized snapshots, captured owners and session transitions.

For lobby-enabled builds, `PawLobbyCompatibility.Version` also supplies the
startup label and feature version. The builder rejects a different version in
`paws_patch_versions.ini`. `--preflight <game-directory>` now executes the same
installed-configuration identity check as normal startup, without extracting the
native DLL or launching the game. Run `InstalledStartupTests.cs` against all eight
built/installed helpers: it checks the real installed version and preflight, and
verifies rejection of an incompatible version using an in-memory state copy.

## Arcane Wars beta.9

Use `-CityAssistant -LobbyCompatibility -LairRecovery -NativeCompiler <x86-tcc>`
for the release build. All eight helpers install the manually accepted lair
survivor r3 payload. `--features` reports `lairWoundedDefendersRevision:3`.
The four release modules carry 11 helper copies; duplicate paths contain
identical current bytes, so optional modules cannot reintroduce older helpers.
The separate common-ui module installs 18 static racial minimap textures fitted
to 16:9. No EXE selects a resolution, generates textures or rewrites HUD files.
