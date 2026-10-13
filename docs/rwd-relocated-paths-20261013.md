# RWD relocated paths: local follow-up, 2026-10-13

First follow-up to Ensrick's [PR #8 review](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/8#pullrequestreview-5501117737).
The regression reproduced the reported failure on the preceding implementation:
opening an archive under an NTFS junction failed with `Links/reparse points are not allowed`.

## Change

- RWD `list`/`verify` open the selected archive once and validate that file handle,
  without walking and rejecting all its parents.
- On Windows, explicit pack/extract roots and output-archive parents may be junctions.
  A held handle resolves the physical directory; strict physical-ancestor locks and
  a volume/file-ID comparison prevent using a replaced directory. Payload paths use
  that physical root. Links encountered inside an input/output tree remain rejected.
- Directory locks request `FILE_LIST_DIRECTORY` as well as attributes. A real rename
  regression demonstrated that attribute-only handles did not prevent replacement.
- Tree packing revalidates each source's parents when it reopens the payload.
  Caller-specified `WriteFiles` sources may use linked parents. Atomic output,
  failed-write preservation, path/name checks and resource limits remain in place.
- Launcher import accepts an archive from a relocated source folder. It still copies
  from one read handle and validates the copied bytes. Managed cache link checks remain.

This is RWD-tool/source-import compatibility. It does not relax the general game
installer, rollback, cleanup or uninstall path rules. In particular, an installed
game selected through a linked path is still subject to those separate rules.
No Documents-depot compatibility report, lobby admission change or runtime patch is added.

The [Windows final-path API](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-getfinalpathnamebyhandlew)
resolves the opened target. Directory sharing follows
[CreateFile](https://learn.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-createfilew).
Callers must control the operation tree; direct in-place permission/reparse-metadata
mutation by another process is outside the documented concurrency contract.

## Validation

- 154 focused assertions: 79 archive, 22 relocated-path and 53 launcher custom-mod checks.
- 37,542 assertions in the full launcher suite.
- Real junctions cover selected roots, ancestors, chained links, missing destination
  directories, byte-identical output and input/output aliases. Nested and dangling
  links remain rejected and outside sentinel files remain intact.
- During a held operation, a physical-root rename is rejected; retargeting the original
  alias does not change the write destination or an already-open archive's bytes.
- A nested source directory swapped to an outside junction between enumeration and
  payload reading is rejected without replacing the existing output archive.
- Read-only CLI verification through a temporary junction to the actual Steam game:
  Data.rwd 10,526 entries / 385,157,324 payload bytes; Music.rwd 31 / 86,957,244.
- The self-contained local test EXE reached its WPF window using a separate smoke
  profile with no error log. No game process was started.

Evidence: workspace `outputs/rwd-relocation-20261013/`.
Test launcher: `launcher/PawsLauncher-Rwd-Test.exe`, with `launcher.test-mode=custom-rwd`.
It uses an isolated profile and skips self-update. SHA-256:
`6980FE85CA4913F63AF3494A828F57FDE131A3D87917A1F3B3EB5F93527DA03E`.
This change is local only; public version 0.9.3 and signed feeds remain unchanged.

## Remaining follow-up order

1. RWD format/evidence documentation comments from PR #8: [completed locally](rwd-format-evidence-20261013.md).
2. PR #1/#8 contribution status: [reconciled](ensrick-followup-review-20261013.md#contribution-status).
3. Recovery PR #9 and probes: [reviewed](ensrick-followup-review-20261013.md#recovery-pr-9); PR #11's save-source
   guard is applied and tested locally, with in-game acceptance still pending.

The user-cancelled Documents-depot report stays deferred. Localization and keybinding
differences are not introduced as lobby admission restrictions.
