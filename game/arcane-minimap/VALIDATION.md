# Local validation, 2026-10-29

Not published. Ordinary Steam installation and user saves were not modified.

## Asset and installer

- Original `AWTechMiniMapIcon.png`: 7x7, every pixel transparent.
- Eight Arcane Wars special-site definitions reference this same texture,
  including `AW_shadow_sepulcher` (Shadow Sepulcher / Гробница теней).
- Replacement: unchanged existing mod `MarkerLairTechMapIcon.png`.
- Built an additive `common-ui` candidate from published `0.4.2-beta.8`;
  all 104 existing payload files are unchanged. No gameplay definitions changed.
- Production `ModuleInstaller` integration fixture passed payload validation,
  installation, original-file backup, removal, exact restoration of original
  bytes, and removal of the module's ownership entry.
- Isolated game `D:\PawReplayQA-20261029\game`: loaded the user's copied save,
  located actor 71 / `AW_shadow_sepulcher` at (287,128), and visually confirmed
  its minimap marker. In this earlier save it is still owned by the neutral
  Shadows kingdom. The later friend-captured state was not available in that
  save, so that specific owner transition has not been visually verified.

Evidence is in the workspace `outputs/replay-fix-20261029` directory:
`minimap-visible.jpg`, `minimap-world.json`, `installer-check`, and
`common-ui-minimap-local.zip`.

Candidate SHA256:
`5253EAD3864BCCE61ACF03B1433991B96FBDF5C27BE3CEB527AD19B8AC64FF38`.

## Separate replay corrections

The icon change is visual-only. Two independent replay defects were reproduced
and fixed in the game helpers:

1. Source kind 2 was incorrectly treated as a saved game for all actor
   materialization. Native film state 4 must reproduce fresh-map independent
   family ownership. Serialized saved owners and captured human owners remain
   untouched. All six helper source variants use the same corrected guard.
2. Native film players all have AI=0. The synchronous path policy must use the
   film's serialized kingdom AI profile, matched by internal ID, instead of
   treating recorded bots as humans. No native player flags, strategic AI,
   film commands or synchronization checks are altered by the final fix.

Unchanged native source rebuilt byte-identically to the published Arcane and
shared Vanilla/Immortals payloads, confirming that the new payloads introduce
only this routing change. All 12 helper builds preserve published feature flags.

Completed checks on final local builds:

- Saved-owner/film-owner native regression: 303240 assertions on four Arcane
  family-enabled helpers, and 303240 on four shared helpers, three image bases.
- Film routing: 102 cases plus 2958 ABI/write-bound assertions per native profile.
- Existing Arcane routing regression: 15285 assertions.
- Existing host/client parity regression: 189 cases plus 2358 route ABI checks.
- Shared native profile boundaries: 509 assertions.
- New owner and routing regression cases fail against the published binaries.
- Original supplied six-player film reaches the native END_OF_FILM on the final
  built helper, at accelerated playback. All native film player AI flags stayed
  zero. The earlier diagnostic-only flag edit was not present in this process.
- The previously failing `trace6` three-bot recording also reaches END_OF_FILM
  on the final helper at normal speed, in the same process after the original.
- The previously failing `trace4` recording reaches END_OF_FILM at 400% speed
  in the same final-build process, completing the third replay without restart.
- No FILM OUT OF SYNC appears in that final process log.

Evidence: `original-final-build-end.png`, `final-native-film-flags.json`,
`trace6-final-build-end.png`, `trace4-final-build-end.png`, `final-live-log.log`,
`final-helpers/build-report.json`, `final-pure-helpers/build-report.json`,
`ai-native/film-routing.json` and `pure-ai-native/film-routing.json` under
`outputs/replay-fix-20261029` in the workspace.

This validates the supplied recording and controlled regression fixtures, not
arbitrary films recorded with different gameplay packages/settings. Live
Vanilla/Immortals playback and the friend's later captured-building state were
not reproduced. No publication or ordinary installation changes were performed.
