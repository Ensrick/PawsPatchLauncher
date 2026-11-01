# Local mod-manager integration and idle CPU investigation — 2026-11-01

Request: restore the previously deferred manager from Ensrick's #17 and investigate his report of a saturated CPU core. No release or remote repository changes made.

## Sources and scope

- Active checkout: `work/launcher-104-release`, starting HEAD `a397bd6` (launcher 1.0.6).
- Reused the local October 26 candidate in `work/recovery-beta-release`, documented by `docs/verification-custom-mod-page-2026-10-26.md`. Only its separate custom-mod page and associated guards/tests were ported, not unrelated installation, runtime or publishing changes.
- Reviewed PR #17 at head `93890ba6e70ca1cc135a1d74feeab5eda2cacd65`: https://github.com/VasiliyPaw/PawsPatchLauncher/pull/17 . Adapted its `InitialFeed` and legacy-game fixture to current controls and public Arcane Wars access. The existing file-only installer was retained.
- Separate Mods navigation exposes RWD import, replacement, enable/disable, priority, overlaps and apply/undo. Games and installation remain under Components, with a direct link from Mods.
- Dirty drafts survive repeated navigation to Mods; navigation away and launch require apply/undo. Import guards prevent duplicate file pickers and close/reentry during work. Existing game settings remain separate.
- Applied data-only installations display their mode and detected game version. The compatibility dialog now says “File changes available” for this mode instead of implying that the entire game cannot run. Native compatibility gates remain unchanged.
- Existing unrelated social/monitor/database work was preserved. The local EXE includes the current checkout, including its pre-existing social changes; it is not an isolated public release of only this feature.

## Validation

Evidence folder relative to project root: `outputs/mod-manager-20261101`.

- Full console suite: **38,206 assertions passed** (`tests.log`).
- Custom-mod UI: **96 assertions each in en/ru/de/fr/cs/uk**. Cancel/error/reentry, archive operations, order, conflicts, draft retention, apply/undo, game-running and launch guards. Rendered 1050- and 1440-wide layouts. Russian and German compact screenshots visually inspected; labels and fixed actions fit.
- Legacy 1.3.68: **74 assertions each in en/ru**. Real launcher install/apply controls, locally signed ZIP feeds, synthetic non-executable version fixture; Vanilla/Paw's Patch, Immortals and Arcane Wars file-only installation, RWD mount declarations, original-executable launch eligibility, native module exclusion, offline switch back. No native game launched by this test.
- Existing mod-library suite: **276 assertions**, including 45 offline mode clicks (max 19.7 ms, mean 9.3 ms), offline language/component switching, installed-release preservation. Updated two obsolete account-entitlement expectations for already-public Arcane Wars; offline update button and network rejection checks remain.
- Game settings: **42 assertions ru**. Compatibility: **39 assertions each en/ru**, including the clarified file-only heading and unchanged native gates.
- Build succeeds with the existing CS0649 warning for `_socialPresencePublishOverride`; no build errors. `git diff --check` passes.
- Self-contained renamed EXE reached `window-ready.txt` (1.0.6) under a disposable smoke profile. Own test processes terminated after inspection. No installed user game was launched or modified during this turn.

## CPU measurements and limitations

No user launcher was running when investigation began. A copy of the pre-edit build was launched under an isolated guest profile against an existing isolated Immortals fixture. Normal background work was enabled. A 15-second measurement used 0.46875 CPU seconds, **3.125% of one core**. A 20-second managed trace is saved as `baseline.nettrace`; sampled stacks were mostly waits, not a tight application loop. These are wait-inclusive samples, not percentages of consumed CPU time.

Added `--idle-cpu-probe` to PreviewRenderer for repeatable page measurements; it requires an explicit test profile without smoke mode, runs normal timers, uses empty feed endpoints and does not install or download packages. `cpu-pages.json` records process CPU time divided by elapsed time (100% means one fully occupied logical core):

| Page | One-core utilization | Sample seconds |
|---|---:|---:|
| Home | 19.35% | 10.33 |
| Components | 3.91% | 10.40 |
| Mods | 2.39% | 10.46 |
| Friends | 0.90% | 10.39 |
| Settings | 17.18% | 10.82 |
| Guide | 2.11% | 10.37 |

Maximum dispatcher delay was 280 ms on Settings and at most 17 ms elsewhere. The one-core saturation report was **not reproduced**. No speculative CPU fix was made. Measurements used hidden/offscreen windows and guest profiles; visible composition, authenticated animated avatars, Ensrick's custom build, and his exact session remain untested. Existing data does not justify claiming the issue fixed or absent in those conditions.

## Local artifact

`outputs/mod-manager-20261101/launcher/PawsLauncher-ModManager-Test.exe`

SHA-256: `CA631D6D3809A2689E5329264B35612B7539A046B6D23FD9E063AFE9FA0CF48C`

Version remains 1.0.6; distinct assembly name and `launcher.test-mode` (`custom-rwd`) separate its profile. The marker isolates launcher settings, not the game folder: manual installation acts on whichever folder is selected. Keep the artifact folder together. No version bump, feed publication, PR merge or comment was performed.
