# Launcher gold icons and integrated beta HUD — 2026-10-13

Release identities: launcher **0.9.6**, Arcane Wars **0.4.2-beta.1**,
Vanilla/Immortals **0.4.1-beta.3** (runtime **1.3.72-pure.15-beta.3**).
Ensrick's PRs #13 and #14 are retained in merge history. The exposed widescreen
checkbox from #14 was replaced with integration inside Paw's Patch.

The rejected fixed-width HUD is not packaged. The final panel spans the screen,
with original minimap art left, action controls right, and the object display
between them. Stock art is split by the native tiled-decal renderer; only two
original centre columns repeat. The Arcane Wars layout retains its own compact
company spacing; Vanilla and Immortals use stock spacing.

## Evidence

- Launcher Release suite: **37,574 passed**.
- Isolated account/database suite: **12,493 passed**, no production mutations.
- WPF icon rendering: **1,370 passed**, 47 icons, seven sizes and four DPI
  scales; includes real enabled/disabled button styles and explicit colours.
- Native uniform-layout emulator: **612 passed**, three ASLR bases, nine
  dimensions, both stored flags and both cached states; checks stack balance,
  layout-change polling and unchanged user flags.
- Native/legacy presentation composition: **641 / 605 passed**.
- Resource builder: **5,483 checks**, seven skins, three atlas resolutions,
  pinned source hashes and lossless crops; stock coordinates preserved at 4:3.
- All eight Arcane Wars and four Pure helpers built successfully. Existing AI,
  transfer, recovery and other native regression suites passed during builds.
- Pure integration matrix: **16 actual native menu launches**, six text
  languages for each mod, component combinations, stable rollback and disable.
  Native executable SHA-256 remained
  `1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45`.
- Arcane Wars: actual menu launches with AI on/off/on, verifying live difficulty
  definitions and selected helpers. Separate stable/off reconciliation verifies
  removal of new overrides and restoration of the base mod's object display.
- Interactive matches: Vanilla 1280×1024, Immortals 2560×1080, Arcane Wars
  1920×1200 and 1600×900. Selection and panel placement were inspected in the
  running game; the Immortals right-hand settlement tab was opened. Games exited
  normally. The Arcane Wars 1600×900 check uses the final mod-specific layout.

Native read-only probes observed effective logical widths 1024, 1820.444 and
1228.8 for the first three resolutions, with UI height 768. The saved flag was
not modified. A separate startup with a manually supplied widescreen flag also
retained the 1024 minimum width on 1280×1024.

The test fixture and redirected Documents profiles are under
`D:\PawWidescreen-20261013`; native screenshots, logs, hashes, staged feeds and
package audits are under `outputs/widescreen-release-20261013` in the parent
workspace. The user's three real preference files were hash-checked unchanged.
No VM recording was used.

These are UI/startup/install checks, not a new long-running multiplayer test.
No simulation change is included. An earlier exploratory generation crash in a
different private profile was not reproduced in the final interactive cases;
its cause was not established and no speculative generation fix was added.

## Scope and publication

Only seven beta packages change. The package audit preserves every unrelated
payload byte, removes the 21 former loose frame fillers per family, and adds 24
UI overrides. Pure gameplay exclusions and recovery revisions remain intact.
Its final package hashes are identical to the already verified Pure tag.
Stable game packages are unchanged; launcher catalogs are updated separately.
No server migration is needed for these UI-only changes.

Release tooling verifies uploaded asset hashes and tag commits before promoting
the signed beta feed, then verifies the four public launcher catalogs and
history against the tagged CI artifact. Public release notes contain only the
user-facing changes; this document holds validation details.
