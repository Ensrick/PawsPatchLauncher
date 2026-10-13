# Custom RWD mods: local prototype, 2026-10-13

Not published. The settings gear now has **Game settings** and **Custom mods** tabs.
The test build excludes the cancelled Documents-depot scan and report UI experiment.

## Behavior

- Import a validated Kohan II TGCK v2 `.rwd`, up to 2 GiB. The source file is never
  edited or deleted. Imported mods start disabled.
- Enable/disable, reorder, or remove entries, then **Apply mods**. Cancel discards
  the draft. Vanilla, Immortals and Arcane Wars have separate lists, each shared
  between that game's channels. Up to 32 archives per list.
- Last enabled entry wins among these mods. Generated paths use content hashes,
  independent of source names. Archives mount after the normal startup script;
  special engine depot priorities still apply.
- Choices stay in installation state, outside configuration codes and friend offers.
  Imports are stored in `.pawpatch/custom-rwd/`; enabled copies are in `PawsCustom/`.
- A generated `local-custom-rwd` module supplies the extended startup script and
  active archives. Existing installation transactions back up/restore all of these
  files and the selection. Disabling restores the current game's startup file;
  main-game updates recompose it from the new package.
- Mod-only application preserves personal hotkeys. Other damaged main-game files
  require repair instead of being silently overwritten by a mod operation.
- Running game, stale installation state, invalid archives, duplicate content,
  invalid profile/hash identifiers, and missing/changed cached archives are rejected.
  Import/application run off the UI thread. Closing is blocked during these operations.
- The launch integrity check includes enabled RWD files. The existing multiplayer
  report includes archive bytes and order via the generated module. No additional
  lobby admission gate was added. Cross-peer equivalence of different archive
  packaging or localization-only archives is not established by this work.

## Evidence

Stock Steam 1.3.72 EXE SHA-256:
`1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45`.
Private game copy and private Documents profile; the normal game remains unchanged.
Fixture includes the 1.3.72 options-dialog correction already shipped by the launcher.
Without it, stock Data.rwd alone fails at `AutoSaveOptionsPanel`.

Two test archives replace the same main-menu label. Observed in real game windows:

| Registration order | Observed label |
|---|---|
| A, B | RWD_B |
| B, A | RWD_A |
| A, loose directory | LOOSE |
| Loose directory, A | RWD_A |
| Installer-generated A, B mounts | RWD_B |
| Installer disables both | Single Player |

These are resource-loading/startup checks, not multiplayer or gameplay certification.

Output: workspace `outputs/rwd-engine-20261013/`. `export-test-source.py` exports
RWD-only source from draft PR #8 plus this feature, excluding the cancelled report.
Clean source: **37,510 PASS**, including **47 custom-mod assertions**. Custom-mod UI:
**19 checks in each of six languages (114 total)**. Existing settings UI: **42 checks**.
Checks cover invalid input, lifecycle, profile switching, update rebasing, rollback
after a simulated mid-install interruption, persistence, and personal hotkeys.

RWD reader/writer originated in Ensrick's PR #1, hardened in draft PR #8, under this
repository's MIT license. Feeds, server and published launchers are unchanged.
Reconnect work is separate.

### Local test follow-up: file-picker cancellation

The renamed test EXE exposed a hardcoded `PawsPatchLauncher` assembly name in
chat-glyph resource URIs. Returning from the native file picker activated the
launcher and redrew chat; the missing resource assembly raised an exception.
Closing its error dialog activated the window again, repeating the failure.
Glyphs now resolve their owning assembly. The RWD picker also has an explicit
window owner, runs within error handling, and blocks reentry until it closes.

The new glyph regression failed against the old renamed assembly with the same
exception. After the fix: **55 custom-mod UI checks** pass for the renamed test
assembly (RU) and **55** for the normal assembly (EN), including all 26 glyphs,
cancellation, retry after a picker failure, and reentry. Existing game-settings
checks: **42 PASS**. In the published local test EXE, both the native Cancel button
and Escape returned to the unchanged mod list, and the error log did not grow.
The local artifact was replaced; no public release was made.
