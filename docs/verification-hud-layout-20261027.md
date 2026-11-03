# Widescreen HUD revision 3

Release scope: Arcane Wars 0.4.2-beta.8 and Vanilla/Immortals
0.4.1-beta.10 (runtime 1.3.72-pure.15-beta.10). Launcher remains 1.0.4.

The production candidates were rebuilt from the previous beta input manifests,
with only presentation code and version metadata changed. All embedded resource
names and bytes are identical to the previous production helpers (eight Arcane
Wars and four Pure variants). Crash guards, recovery and lobby-startup resources
are preserved. No simulation change is included.

The HUD payload contains 67 resources per family. All previous payload files
outside the explicit helper/UI scope remain byte-identical. The sole new loose
override is `data/ui/game/time_message.tgi`, derived from hash-pinned retail data.
The normal module manifest manages its installation and removal on rollback.

Automated validation on the release source and candidate artifacts:

- Asset builder: 5,484 assertions; both rebuilt manifests match the staged assets.
- Presentation install/rollback: 1,057 checks in disposable processes.
- Emitted native bounds/tooltip code: 16,200 checks, three relocations and nine
  resolutions, preserving registers, flags and stack.
- Selection/banner/formation layout: 482 checks for both families, seven
  resolutions and one through six selected companies.
- Original native panel picking: 2,344 checks.
- Panel alpha: 326,994 checks across seven skins and three texture variants.
- Emitted match-phase/button code: 738 checks.
- Embedded resources: unchanged in all 12 production helpers.
- Installation/rollback: 21 cases in an isolated game copy.

The user requested publication after the local UI iterations. Exact final-build
manual gameplay/click acceptance is not claimed by these automated checks.
Native menu-launch evidence, CI results, signed-feed promotion and public asset
readback are recorded separately in the release output directory.

Publication uses `tools/PrepareHudLayoutBeta.py`. It updates only the configured
v3 beta and backward-compatible v2 beta catalogs and verifies the stable and
legacy catalogs remain unchanged. Promotion requires passing installation,
three actual menu launches, native regressions and exact package hashes.
