# Paw's Launcher

Windows launcher and transactional updater for **Kohan II**, **Immortals**,
**Arcane Wars**, and optional **Paw's Patch** components. Built with C#/.NET 8 and WPF.

[Download the launcher](https://github.com/VasiliyPaw/PawsPatchLauncher/releases/latest/download/PawsPatchLauncher.exe)
· [Releases](https://github.com/VasiliyPaw/PawsPatchLauncher/releases)
· [Contributing](CONTRIBUTING.md)
· [Developer guide](docs/DEVELOPMENT.md)

## For players

- Manage mods, text/speech languages, and independent Release/Beta patch channels.
- Install verified packages with backups, rollback, and compatibility checks.
- Find players through EN/RU community chat, friends, and private messages.
- View shared lobby/match details and send saves or copy a friend's game configuration.
- Set resolution, FPS limit, audio, and selected interface settings before starting the game.

A separately installed copy of Kohan II is required to play. Gameplay options vary
by mod and channel; the launcher's Components page and guide show availability.
The latest version and changelog are on the releases page, rather than pinned here.

## For contributors

The repository is public. Fork it and open a pull request against `main`; no
production credentials, signing keys, or collaborator invitation are needed to
build and run the automated launcher tests.

Requirements: **Windows**, the **.NET 8 SDK** (not just the runtime), and
**Node.js 24 with npm** for the isolated database tests.

From the repository root:

```powershell
dotnet build src/PawsPatchLauncher/PawsPatchLauncher.csproj -c Release
dotnet run --project tests/PawsPatchLauncher.Tests/PawsPatchLauncher.Tests.csproj -c Release
npm --prefix tools/AdminDatabaseTests ci --ignore-scripts --no-audit --no-fund
node tools/AdminDatabaseTests/run.mjs
```

The C# tests are an executable test harness: use `dotnet run`, not `dotnet test`.
Database tests use a local, disposable PGlite database, not production Supabase.
NuGet/npm dependencies need network access on the first restore. The standard
checks do not require an installed game or a running multiplayer session.

See the [developer guide](docs/DEVELOPMENT.md) for the code map, isolated UI
previews, and the separate requirements for native game patches. The
[RWD, plugin API, and reconnect collaboration notes](docs/RWD_AND_RECONNECT.md)
explain the proposed starting points for those contributions. These are proposals,
not an announcement of shipped RWD packing or reconnect support.

## Repository layout

| Path | Purpose |
| --- | --- |
| `src/PawsPatchLauncher/` | Launcher UI, mod installation, updates, accounts, chat, game settings and activity |
| `tests/PawsPatchLauncher.Tests/` | Automated C# checks and synthetic fixtures |
| `tools/AdminDatabaseTests/` | Isolated database/migration checks |
| `tools/PreviewRenderer/` | WPF previews and focused UI checks |
| `game/` | Version-specific game patch code and research tools |
| `supabase/` | Server migrations and functions; deployment is separate |
| `feed/` | Published catalogs, package identities, and changelogs |
| `docs/` | Release notes, design notes, and validation records |

## Updates and release ownership

Packages contain a manifest and per-file hashes. Signed catalogs identify the
allowed downloads; installation uses ordered overlays and transactional backups.
Disabling a module restores the appropriate lower layer. Launcher self-updates
include restart checks and rollback.

`feed/v2/stable.json` and `feed/v2/beta.json` are the current catalog endpoints.
Legacy catalogs remain for older launcher clients. Feed changes, package
publication, server deployments, and release tags are maintainer operations;
ordinary pull requests do not publish an update. Never commit private signing keys.

Catalog signatures and Windows executable signing are different mechanisms. See
the [code signing policy](docs/CODE_SIGNING.md) for the current executable signing
status, and the [privacy policy](docs/PRIVACY.md) for accounts, chat and diagnostics.

## Authors and acknowledgements

- **Paw** — Paw’s Launcher / Paw’s Patch development, adaptation and integration.
- **[Ensrick](https://github.com/Ensrick)** — [RWD archive tools](tools/RwdTool/README.md),
  [multiplayer save copies and autosaves](game/multiplayer-saves/README.md), and the
  [foundation of match recovery after a disconnection](game/reconnect-test/THIRD_PARTY.md).
  The recovery flow saves the match and returns to the lobby so players can rejoin
  and resume from that save. Thank you for sharing your work and collaborating!
- **Darquan Mortis** — Arcane Wars; **MartialDoctor** — Immortals. These are
  independent mods by their respective authors.

## License and third-party content

Project code is provided under the [MIT License](LICENSE), subject to the notices
for third-party components and assets. Kohan II and third-party mods retain their
own rights; this license does not grant rights to redistribute their executables,
archives, or a complete decompilation. Arcane Wars is a third-party mod by
Darquan Mortis. Use your own game installation for native integration tests.
