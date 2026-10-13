# Contributing to Paw's Launcher

Contributions are welcome through forks and pull requests against `main`.
Please use English for shared development notes so contributors can work together.

## First contribution

1. Fork [VasiliyPaw/PawsPatchLauncher](https://github.com/VasiliyPaw/PawsPatchLauncher).
2. Clone your fork, add the upstream repository, and create a focused branch:

   ```powershell
   git clone https://github.com/YOUR_USERNAME/PawsPatchLauncher.git
   cd PawsPatchLauncher
   git remote add upstream https://github.com/VasiliyPaw/PawsPatchLauncher.git
   git switch -c feature/your-change
   ```

3. Follow [DEVELOPMENT.md](docs/DEVELOPMENT.md) to build and run the checks.
4. Implement one reviewable change. Add a regression check for a behavioral fix;
   include before/after screenshots for visible UI changes.
5. Push your branch to your fork and open a pull request with base `main` here.
   Check the Actions results and address review feedback before merging.

RWD packing, a plugin API, and reconnect work have a dedicated
[starting-point document](docs/RWD_AND_RECONNECT.md). A small demonstrator and
reproduction steps are useful even before a complete implementation is ready.

## Scope and review

- Keep unrelated refactors and formatting changes out of a feature PR.
- Match the existing code style. Keep UI work responsive: file, network, and
  archive operations must not block the WPF UI thread.
- Preserve signatures, hash verification, safe extraction, backups and rollback.
- For user-visible text, cover English/Russian and the additional supported UI
  languages in `src/PawsPatchLauncher/Assets/Languages/`, or identify missing translations in the PR.
- Separate launcher changes from game simulation changes. Vanilla, Immortals,
  and Arcane Wars rules must remain distinct.
- State exactly what was tested: automated checks, local game, VM pair, or real
  multiplayer peers. A successful build does not establish multiplayer sync.
- Native hooks must identify the supported executable and check original bytes;
  an unknown build must fail safely rather than accept guessed addresses.

## What to include in a pull request

Describe the player-facing result, the relevant implementation choices, and the
checks you ran. Include reproduction steps and remaining limitations where useful.
The [PR template](.github/pull_request_template.md) provides a short structure.

For native multiplayer work, include mod/version, peer roles, game settings,
the disconnect/rejoin sequence, and matching state-check evidence if available.
Use small synthetic test data wherever possible. Review logs and dumps before
sharing: they can contain account details, local paths, chat, or process memory.

## Credentials, content and releases

Regular contributors do not need production Supabase access, signing keys, GitHub
release tokens, or access to a maintainer's account. The database suite runs locally.
Do not put credentials in code, PR text, screenshots, or test data. Public keys and
public client configuration are not permission to use privileged server credentials.

Use `.local/` for local game copies, research inputs, and private configuration.
Do not add original game executables, commercial game archives, complete
decompiled game sources, personal saves, crash dumps, or chat exports. Contribute
your own implementation and document the origin/license of external code or assets.
The project's MIT license does not relicense third-party content.

Do not change published signed feeds, release package hashes, version numbers, or
release tags as part of a normal feature PR. Server migrations belong in a PR but
are deployed separately by the maintainer. Tests must never target production.
The maintainer reviews integration and publication; a merged feature is not an
automatic game-package release.
