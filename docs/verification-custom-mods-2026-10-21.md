# Custom RWD manager: first part of Ensrick PR #17

Local candidate based on release 1.0.2. This is **not included in 1.0.2**.
Source proposal: https://github.com/VasiliyPaw/PawsPatchLauncher/pull/17
reviewed head `93890ba6e70ca1cc135a1d74feeab5eda2cacd65`.

## Included

- Resource-name listing and overlap calculation from Ensrick's proposal.
- Enabled load-order numbers; disabled entries have a dash.
- Matching-file counts against earlier enabled custom mods, with up to 20
  matching paths in a tooltip. This does not compare against the base game.
- Reimporting a changed archive with the same unique name replaces that entry
  in the draft, preserving position and enabled state. The row's replacement
  button also accepts a differently named archive.
- Undo restores the saved custom-mod list without closing Game settings.
  Applying still uses the existing installation transaction and rollback.
- Capacity is still 32 mods; replacement is allowed at capacity.
- Russian/English UI text and Czech/German/French/Ukrainian catalog entries.

The existing Game settings / Custom mods tab is retained. No base-game selector
is moved, and no account restriction is reintroduced for Arcane Wars. The ModDB
installation flow, localization/keybind handling and multiplayer checks are
unchanged. The separate manager-page redesign remains for a later preview.

## Interpretation and failure behavior

Matches use normalized, case-insensitive resource names, as in PR #17. They are
advisory and not a proof of identical contents or every engine loader's case
behavior. The UI says "matching files", not confirmed simulation compatibility.
Unreadable enabled archives show a warning instead of silently claiming that
the list has no overlaps. Names are cached only for the current settings session;
the installer's independent hash checks remain authoritative at apply time.

Canceling the file picker does not dirty the draft. Invalid replacements leave
the previous entry intact. Imported archives remain in the managed cache when a
draft is canceled, as before; they are not mounted until Apply succeeds.

## Verification

- Full launcher suite: **38,188 passed** (includes pre-existing Arcane source
  tests in this checkout).
- WPF checks: **80 per language, 480 total** across ru/en/de/fr/cs/uk. Covered
  picker cancellation/reentry, invalid archive, overlap enable/disable/reorder,
  badges, draft-only replacement, Undo, applying/reopening, game-running guards,
  rejecting a 33rd mod, and replacement at capacity.
- Build: zero errors/warnings. Self-contained candidate built successfully.
- Two window-start smoke checks passed, including standalone EXE use.
- Rendered Russian and German dialogs inspected at the existing small-window
  test size; all six language renders retained. No game or VM was started.

Evidence under task workspace `outputs/mod-manager-20261021/`: tests.log,
ui-LANGUAGE.log, custom-mods-enhanced-LANGUAGE.png, build.log, publish.log,
smoke.log. Candidate: `launcher/PawsLauncher-Mods-Test.exe`.

On 2026-10-22 the user delegated the remaining check and authorized publication.
Release validation is recorded in release-1.0.3-validation.md. No new in-game or
multiplayer acceptance is claimed. Existing archive mounting is unchanged.
