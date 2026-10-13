# Developer guide

## Build and test from a fresh clone

Use Windows with the .NET **8 SDK**, Git, and Node.js **24** with npm. Check
`dotnet --list-sdks`: installing only the .NET runtime is not sufficient.
PowerShell examples below run from the repository root and contain no
maintainer-specific paths.

```powershell
dotnet build src/PawsPatchLauncher/PawsPatchLauncher.csproj -c Release
dotnet run --project tests/PawsPatchLauncher.Tests/PawsPatchLauncher.Tests.csproj -c Release
npm --prefix tools/AdminDatabaseTests ci --ignore-scripts --no-audit --no-fund
node tools/AdminDatabaseTests/run.mjs
```

The C# suite is a console test executable, not a test-adapter project. `dotnet test`
is not a substitute for running it. Check the exit code as well as the PASS output.
The database runner creates a disposable PGlite database and applies local migration
fixtures; it needs neither a Supabase account nor a production database connection.
First-time NuGet/npm restore requires network access.

These commands build/test the launcher. They do not compile every native patch or
launch Kohan II. Python and native compilers are not needed for this baseline.
GitHub's **Build and test** workflow runs the same checks for pull requests.

## Code map

| Area | Starting points |
| --- | --- |
| WPF shell and pages | `src/PawsPatchLauncher/MainWindow.xaml`, `MainWindow.*.cs`, `App.xaml` |
| Mod/channel selection | `GameMod.cs`, `GamePackageSelector.cs`, `EffectiveSettings.cs` |
| Installation and verification | `ModuleInstaller.cs`, `CryptoAndIO.cs`, `FeedClient.cs`, `Models.cs` |
| ZIP module packaging | `tools/PawsPatchPublisher/Program.cs` (`pack`) |
| RWD archives and custom mods | `tools/RwdTool/`, `src/PawsPatchLauncher/CustomRwdMods.cs`, `docs/custom-rwd-mods-local.md` |
| Social features | `Account*Service*.cs`, `CommunityChat.cs`, `MainWindow.Community*.cs` |
| Local game configuration | `GameUserSettings.cs`, `GameResolution.cs` |
| Reading game activity | `KohanActivityReader.cs`, `GameActivity.cs`, `GameActivityClock.cs` |
| UI language strings | `Localization.cs` and `Assets/Languages/` |
| Native transfers / sync diagnostics | `game/fast-transfer/`, `game/beta7/SyncDiagnostics1372.cs` |
| Runtime patch entry points | `game/pure-fixes/`, `game/beta7/` |
| Multiplayer saves (save copies, multiplayer autosave) | `game/multiplayer-saves/` |
| Account database | `supabase/migrations/`, `tools/AdminDatabaseTests/` |

Paths without a directory prefix in this table are under `src/PawsPatchLauncher/`.
`PawsPatchPublisher` currently produces launcher ZIP modules; it is not an RWD writer.
`KohanActivityReader` reads game state for presence; it is not a reconnect protocol.

## UI previews

For a rendered UI fixture without signing into the production service:

```powershell
New-Item -ItemType Directory -Force .local/previews | Out-Null
dotnet run --project tools/PreviewRenderer/PreviewRenderer.csproj -c Release -- .local/previews/home-en.png --smoke-test --language=en
```

The preview tool uses synthetic fixture data and a temporary smoke-test profile.
Its images demonstrate UI layout, not a live multiplayer session. Focused check
switches are listed in `tools/PreviewRenderer/Program.cs`.

Starting the ordinary launcher is different: the default configuration points at
the public feeds and account service, and the launcher can update itself. Do not
use the ordinary app as a substitute for an isolated test. A `--test-profile=...`
argument isolates launcher profile data, but does not by itself block networking,
game-file writes, or isolate Kohan's Documents folder. Use an isolated game copy
and Windows test account/VM for installation and native multiplayer experiments.

## Native game patches

Native game work has extra inputs and is not covered by the baseline launcher CI.
Read the target feature's `game/*/README.md` and build script before running it.
Some older scripts still refer to maintainer-local tools, analysis files, or game
archives. Those inputs are not included in a public clone. Do not run a native
release script by guessing replacements: first make input paths explicit and
test with an authorized local game copy.

Some helpers use the .NET Framework x86 compiler, Python scripts and/or a 32-bit
native toolchain rather than the launcher's .NET 8 toolchain. Keep executable
version/hash checks and hook guard bytes intact. Record which build was tested.

For multiplayer changes, test both peers with identical builds and settings,
check host/client roles, saves/replays and disconnect handling, and compare state
evidence rather than relying only on what the screen shows. See the
[RWD/API/reconnect notes](RWD_AND_RECONNECT.md) for proposed test stages.

## Releases are separate from development

Pull-request checks have read-only repository permissions and do not use production
secrets. They do not deploy Supabase or publish packages. The separate release
workflow handles launcher tags; signed game catalogs and native packages have their
own maintainer-controlled preparation and validation. Do not push a `v*` tag to
test a contribution. The [signing policy](CODE_SIGNING.md) describes publication.
