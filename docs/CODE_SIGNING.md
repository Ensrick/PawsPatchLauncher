# Code signing policy

## Current status

Launcher 0.9.6 is **not Authenticode signed**. SignPath Foundation
admission and production signing are not yet approved. Preparing a signing
workflow does not change the trust status of existing downloads.

We are preparing to apply for free code signing provided by
[SignPath.io](https://signpath.io), with a certificate from
[SignPath Foundation](https://signpath.org). This attribution becomes effective
only after acceptance and a verified signed release; it is not a claim of current
SignPath endorsement.

## Scope and responsibility

- Maintainer, committer, reviewer and proposed signing approver:
  [VasiliyPaw](https://github.com/VasiliyPaw).
- Signing covers `PawsPatchLauncher.exe`, built from this repository's MIT-licensed
  launcher sources. It does not cover Kohan II, Arcane Wars assets, modified game
  executables or separately downloaded gameplay packages.
- Admission must consider the launcher's purpose as a mod updater, including its
  downloads of separately licensed game/mod content. The launcher's MIT license
  does not change the rights to that content.
- Production signing requires maintainer approval in SignPath and multi-factor
  authentication on the maintainer's GitHub and SignPath accounts. Account setup
  still needs verification; this document does not assert it is complete.
- No private key or signing token belongs in source control or releases.

## Build and publication

The Windows GitHub Actions workflow tests and builds the launcher from its source
revision. It uploads the unsigned executable as a GitHub artifact so SignPath can
verify its origin, then requests a signature under the configured production
policy. A signing request is not triggered by a pull request workflow.

When signing is enabled, a missing setting, failed request, unsigned executable,
wrong publisher, wrong product/version or missing timestamp stops packaging.
The ZIP and `launcher-artifact.json` hashes are generated from the final executable
**after signing**. Signed update feeds must subsequently reference those exact
bytes. Feed signatures and Windows Authenticode signatures protect different steps.

Manual workflow runs create downloadable artifacts only. Tag runs create public
releases. Existing releases and feeds are never rewritten to retrofit a signature;
the first signed release needs a new launcher version and tag.

SmartScreen also considers reputation. A valid signature does not guarantee that
every new build immediately runs without a warning. See
[Microsoft's explanation](https://learn.microsoft.com/windows/apps/package-and-deploy/smartscreen-reputation).

## Privacy and removal

See the [privacy policy](PRIVACY.md) for update requests, optional accounts,
presence, chat, avatars, save transfers and diagnostics. The patch and game work
without an account. Signing out disables the social functions. Patch removal,
launcher removal and account deletion are available in the launcher; removing
local files is not the same as deleting a server account.

See [SignPath setup](SIGNPATH_SETUP.md) for the remaining activation steps.
