# Extended selection and group formation

Released in Arcane Wars 0.4.2-beta.10 and Vanilla/Immortals 0.4.1-beta.12; validation scope: ../../docs/release-validation-20261029.md.

Selection revision 3 includes adaptive card paging, wheel scrolling, destination
flags for all selected companies, and group formation revision 1. The selection
module itself remains revision 2; revision 3 adds the formation hook.

## Group destinations beyond six companies

The stock combat, skirmish and column descriptors each contain six points.
`GGroup::CalculateFormation` falls back to location-dependent offsets for further
members. The hook at RVA `0x2337ba` extends only those three exact native point
patterns, continuing their existing rows and spacing. Native centering, direction
and downstream movement remain in use. Groups of at most six, unknown custom
formations and the no-formation fallback retain original behavior.

This is simulation behavior: multiplayer peers and replay playback must use
matching patch versions. This change does not establish network/replay acceptance.

Build with `build_formation.py --legacy <legacy-work> --out <native-dir>`.
`test_formation.py` executes the complete original calculation under Unicorn,
mocking object resolution and the original overflow fallback. 789 assertions cover
three relocated images, all three formations, 1/2/6/7/8/10/14/20/64/256 members,
unique positions, native centering, repeatability and unchanged custom layouts.
`FormationTransactionTests.cs` passed 180 assertions for guarded installation,
partial-write failure rollback and retained allocation on uncertain rollback.

The isolated QA helper includes the preceding group-attack recovery fix. It does
not modify installation catalogs or published game packages. Evidence and helper
inputs are under `outputs/selection-scroll-20261029/formation-native` and
`formation-helpers` in the workspace.

## Destination mask radius (local revision 1)

Native `GGroup::GetOffset` clips positions against a group-owned circular mask
whose radius normally remains 15. Extended rows can leave this circle, causing
otherwise correct offsets to collapse onto nearby grid cells. The hook at RVA
`0x29941f` expands the mask to the farthest centered formation slot plus four
world units for native grid rounding. It covers both initial and recurring mask
updates, while preserving native negative/zero disabled-mask radii. It applies
only to GGroup owners with 7–256 members and the three recognized stock layouts.
Groups up to six, foreign owners, custom layouts, and larger existing radii are
unchanged. Native pathfinding and the group's separate membership radius remain
unchanged.

`build_radius.py` requires `--legacy`, `--out`, and `--compiler`.
`test_radius.py` passes 984 native emulation assertions, including relocated
images, floating-point/register preservation, disabled masks and scope guards.
`RadiusTransactionTests.cs` passes 81 installation/partial-write rollback checks.
Live isolated QA verified 13 exact destination offsets, then all 13 companies
arriving within the native 0.5-unit tolerance and remaining there for 43 simulated
seconds. The test helper includes this fix; public packages are unchanged.
Multiplayer and replay acceptance remain separate release gates.
