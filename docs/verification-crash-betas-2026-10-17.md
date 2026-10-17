# Compact diagnostics and animation Stop guard

Release source: `f717a84ad78f8d25412ae26c8c50932e1b3553be`.
Identities: launcher 0.9.7, Arcane Wars 0.4.2-beta.2,
Vanilla/Immortals 0.4.1-beta.4 (runtime 1.3.72-pure.15-beta.4).

## Scope

The friend's October 15 log-70 dump invokes an animation-controller Stop slot
through a recycled NiMaterialProperty object; the destination is 0x62. A bounded
probe accepts only the pinned executable's 36 NiTimeController-family vtables,
a positive reference count and an executable Stop destination. Invalid entries
skip that call without changing ownership, array entries or reference counts.
This protects the demonstrated call. The underlying object lifetime defect and
the separate NVIDIA driver crash are not established as fixed.

The recorder also captures execute access violations outside graphics modules,
still preserves original exception handling and records at most one full dump
per run. It does not suppress game or driver faults.

Launcher archives retain three recent ordinary log/dump sets, one full memory
dump, required crash companion logs and current status. Text logs over the cap
retain their first 64 KiB and last 4 MiB with explicit truncation metadata.
Dumps remain byte-exact and source files are not changed. SAI logs and our AI
diagnostic directories are excluded. Our periodic AI snapshots/action recording
are disabled; native gameplay hooks are unchanged. Native SAI file creation is
not disabled by this change.

## Validation

- Full local launcher Release suite: 37,532 checks passed.
- Tagged GitHub CI independently passes the same 37,532 launcher checks,
  12,493 isolated database checks, packaging checks and the release build.
- All eight Arcane and four Pure helpers built; each Arcane helper passes its
  five offline self-test modes. Pure deterministic builds and 816 guarded hook
  composition installs passed, plus channel, random-map and save-copy checks.
- The current native crash payload was regenerated from the pinned executable
  and compared byte-for-byte with the inputs used in both helper families.
  Prior local validation of those exact guard/recorder revisions: 567 emulator
  cases, 1,081 transaction checks, 95 real Windows probe checks and nine recorder
  scenarios. AI runtime tests passed 66,580 checks for each mod-family profile.
- Twelve new native-menu launches: Vanilla and Immortals in each of six text
  languages, with varying component choices. The matrix also recognizes four
  previously verified unchanged stable/disabled cases; those are not counted
  as new launches.
- Three new Arcane native-menu launches pass with AI on/off/on. Read-only probes
  confirm the expected difficulty definitions. Total new launches: 15.
- A separate fresh reconciliation/preflight run passes all 16 Pure installation
  cases, including beta-to-stable rollback and disabling the patch for both
  mods. The original k2.exe hash is unchanged.
- Arcane beta-to-stable reconciliation and patch-disabled installation pass
  with the original k2.exe intact.

Launch tests use the marked isolated fixture D:/PawWidescreen-20261013 and private
Documents profiles, not the user's installed game. Only verified test processes
are stopped after observing the main menu. No VM recording or new long-running
multiplayer match is part of this validation.

## Package and publication checks

The staging audit changes six packages: four Arcane helper packages and the
shared Pure runtime/data identities. No payload files are added or removed.
All unrelated payload bytes, gameplay settings and UI data are preserved;
only helper EXEs and the Arcane patch-version INI change. The Pure data package
gets a version bump with identical payload bytes. Stable game feeds remain
unchanged. Launcher feeds are promoted separately after the tagged CI artifact.

Preparation records the baseline hashes, signed test feeds and each asset hash.
Promotion requires installed-build proof, public release/tag verification and
byte-for-byte public asset verification before signing the beta catalog.
Evidence is retained in the parent workspace's
outputs/publish-crash-betas-20261017 directory.
