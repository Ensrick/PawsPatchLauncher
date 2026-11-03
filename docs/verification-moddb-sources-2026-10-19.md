# Arcane Wars split sources — local test, 2026-10-19

## Scope and status

Base Arcane Wars 0.82 beta comes from the author's ModDB download. Darquan
Mortis's original 0.82.1.8 fix v2 RAR is preserved unchanged inside a small
launcher bundle with the existing module manifest and attribution. Paw's Patch
continues to use the existing installation flow.

No public release, signed feed, installed game, or VM was changed. The author
patch bundle is staged locally; its public mirror URL has not been published.
The pre-existing network-enum audit work in this checkout was preserved.

## Sources and integrity

- Base page: https://www.moddb.com/mods/kohan-2-arcane-wars-v078beta/downloads/arcane-wars-082beta
- Download page: https://www.moddb.com/downloads/start/249627
- Original base RAR: 79,327,514 bytes;
  `C0E279D88B10FFBE4EBDDBC361E694C89413D1109147DF8FDAB911F330C0DF07`.
- Original author patch RAR: 561,274 bytes;
  `674C6ED16BAE670DD94B978E3250E02A1791DB57E9D016EFDF70949D8CCD791C`.
- Existing reference: signed v2 catalog `arcane-wars`, version
  `0.82.1.8-clean.1`, package hash
  `D187E0DE5920F591CF8668616CC036DCE6D261ECC436C68EBC657C565E2A049C`.
- Deterministic assembled package: 207,955,597 bytes;
  `E562BE5AF62752D61DB27306B4C7623CF30C37F916EB199DF9C5E4CFA0F9FE19`.

All **6,599 installed file hashes** match the current reference exactly.
The assembled ZIP uses uncompressed entries and fixed metadata to keep its
signed expected hash reproducible across builds. Its larger size is local cache
space, not the network transfer estimate. Source archives are cached separately
by SHA-256 and revalidated before use. SharpCompress 0.50.5 reads the RARs;
its MIT notice is included with the launcher.

## ModDB restriction and fallback

A normal Python request obtained the download page, but the actual .NET
launcher request returned HTTP 403 with `Cf-Mitigated: challenge`. Automatic
download is therefore **not confirmed to work on this connection**. The launcher
does not bypass this challenge or silently fetch the base from our old mirror.

On a failed ModDB base request, a dialog offers Open ModDB, Select downloaded
archive, and Cancel. The user completes the normal browser download and selects
the original RAR. The copied bytes must match the catalog's exact size and hash
before assembly. Cancelling file selection keeps the dialog open; closing it
cancels the operation without retry loops. A wrong archive fails verification.
New dialog text is included in RU, EN, CS, DE, FR, and UK.

ModDB download-counter attribution has **not** been verified against the
author's statistics. No live base download was completed by the .NET test.

## Validation

- Release build: 0 errors, 0 warnings.
- Self-contained Windows EXE: 74,381,382 bytes, SHA-256
  `BD58A823104622EF9B1B2EB64B790BBDB116DB0074C5344AC17C57E4B65254D7`.
- Both executable smoke runs passed (sidecar configuration and standalone EXE).
  The first smoke harness attempt looked for its marker in the wrong directory
  because `launcher.test-mode` overrides the smoke profile. The window did open
  without errors; rerunning with that sidecar temporarily set aside passed.
- Full launcher suite: **37,602 checks passed**, including 22 new source checks.
- Real offline fixture: simulated ModDB 403, one original-RAR import, verified
  assembly, cached second request without network, preparation of 6,599 files,
  install and full verification, uninstall restoring original sentinel bytes.
- Two independent assemblies produced the same package hash.
- Invalid mirror host/file ID/protocol/port/query, oversized/challenge HTML,
  cancellation, malformed source hash and wrong imported archive are rejected.

Commands: tests support `--arcane-sources`, `--build-arcane-sources`, and
`--verify-arcane-sources`. `tools/PrepareModDbSourceTest.py` reproduces the bundle,
assembled package, local catalog copies and test configuration from reviewed
original archives and the current reference ZIP. Outputs are under
`outputs/moddb-source-20261019` in the parent workspace.

## Before public rollout

Publish the author-patch bundle and replace the local source path with its
immutable HTTPS URL; keep author attribution and original RAR bytes. Publish a
compatible launcher before enabling source recipes in signed catalogs. Existing
clients cannot assemble recipes: use a new feed generation or an explicit
enforced minimum-client migration, keeping the old feed usable during migration.
Do not simply replace the existing public package with an empty URL list.
Check the browser flow with the author and verify download attribution there.

## Local access follow-up

At the user's request, the local executable is now built with
`-p:ModDbTestBuild=true`. This compile-time flag opens Arcane Wars selection,
installation/update and use to guests and ordinary accounts, hides the red
access-notice button and restores symmetric selector padding. It does not grant
account/admin privileges. Release builds default to the existing role policy;
adding a test-mode sidecar cannot enable this flag. The flagged EXE also uses a
separate test profile even without its sidecar.

WPF checks passed: 21 checks for guest, member and offline access in the test
build; 35 checks for unchanged role gates in the ordinary build. Both included
the common layout/interaction checks. The EXE in the same local launcher folder
was rebuilt; its current hash is recorded in `smoke-public-access.log`.

## Styled download dialog follow-up

Reused the launcher's custom window chrome, icons, GhostButton and GoldButton
styles. The browser/import steps now have numbered cards, a high-contrast primary
button, inline browser errors and a compact Cancel action. RU/EN/CS/DE/FR/UK
strings were updated. The real WPF dialog was rendered and visually checked in
Russian and German (620 x 525); the Russian primary-button contrast was refined
and re-rendered. The newest EXE hash is in `smoke-styled.log`.

The user-supplied mirror URL ending in
`/249627/130/26fc81a35a1f56bbd1f7495ff8019982` was also tested with a .NET HTTP
GET using the launcher user agent, without downloading a response body. It
returned HTTP 403, text/html and `Cf-Mitigated: challenge`, with no redirect.
That exact URL therefore does not solve automatic downloading on this connection.

## Repeat-download control

The local test build now shows Retry download from ModDB under the Arcane Wars
selector. Each click reads the configured test catalog and uses a new cache in
`test-profile/download-rechecks/<GUID>`. It downloads, assembles and verifies only;
it never prepares/applies a module or changes installed-game state. Existing
normal/test caches are preserved. The action is guarded by the compile-time
test flag and hidden from other mods and ordinary builds. The existing download
progress, cancellation and browser/import dialog are reused. Test outputs are
retained for inspection. RU/EN/CS/DE/FR/UK text is included.

The WPF access checks (21) and shared component-layout checks passed; the button
was visually reviewed. The latest executable smoke results/hash are recorded
in `smoke-retry.log`.
