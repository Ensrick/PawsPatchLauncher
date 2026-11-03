# RWD handle validation on FAT/exFAT

Integrated locally on 2026-10-21 from Ensrick's PR #18, head
`b080d5396225bce16e76d4fe9d02aa1fa5a7519c`:
https://github.com/VasiliyPaw/PawsPatchLauncher/pull/18

The two source/test hunks were applied to the current launcher without merging
the older PR branch. No release catalog or application version was changed.

## Behavior

If `GetFileInformationByHandleEx(FileAttributeTagInfo)` returns error 87,
read basic attributes through `GetFileInformationByHandle` on the same open
handle. Other errors still fail validation. Both paths reject reparse points
and an unexpected file/directory type. A successful first query does not call
the fallback. Failed fallback remains a validation error.

The RWD format, load order, game content, network compatibility, language and
keybind handling are unchanged. The existing relocated-root rules still apply:
selected linked parents are supported, nested archive-controlled links are not.

## Local evidence

- Focused RWD suite: **162 checks passed**, including 8 new attribute-query
  cases, NTFS junction relocation, hostile archives, import, mounting,
  unmounting, ordering and rollback.
- Full launcher suite: **38,129 checks passed**. The working checkout includes
  pre-existing Arcane source tests; that full count is not solely this change.
- Read-only verification of installed `Data.rwd`: **10,526 entries**,
  **385,157,324 bytes**, stamp `dbf9c0e8`; no game started or files modified.
- Self-contained Windows x64 test launcher published successfully to
  `outputs/rwd-fat-exfat-20261021/launcher/PawsLauncher-Rwd-Fat-Test.exe`
  under the task workspace. Logs: `tests.log`, `publish.log` alongside it.
- `git diff --check` passed for the two changed code files.

All available local volumes are NTFS. Error-87 behavior was tested through the
injected query-result helper, not an actual FAT/exFAT volume. Ensrick reports
an actual Windows 11 exFAT import/mount/in-game test in PR #18; this was not
independently reproduced here. FAT32 and network shares remain unverified.
No claim of a new visual UI or in-game test is made.

## Follow-up

This is a local integration, not a public launcher update. The next planned
proposal is #17: useful custom-mod improvements first, with any manager-page
redesign shown in a test build for user acceptance. Preserve public Arcane Wars
and its ModDB installation flow, and do not reinstate Documents scanning or
block multiplayer solely because localization/keybinds differ.
