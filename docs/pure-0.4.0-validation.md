# Pure 0.4.0 release validation

Release scope: promote Vanilla / Immortals 0.4.0-beta.2 to 0.4.0, with surviving
lair defenders included in the core patch. Launcher 0.9.2 is the companion
release. Historical pinned feeds retain their optional defender settings.

## Completed on 2026-10-13

- Launcher Release suite: 37,240 assertions.
- Isolated PostgreSQL/WASM suite: 12,493 assertions, including stable and beta
  configuration grammar, presence and configuration sharing.
- Native AI option matrix: 145 cases each for Pure and Arcane profiles.
- Native lobby identity: 24 valid/invalid channel and patch combinations.
- Hook composition: 804 guarded installs across 12 compositions / 3 image bases.
- Random map transaction regression: 477 assertions.
- Four native helpers reproduced byte-for-byte on their second builds.
- Real WPF controls: 376 assertions, two modes, both channels, six languages,
  core switch, unsupported executable and removal of the separate defender card.
- Installation / reconciliation / read-only native preflight: 16 combinations.
- Native main-menu startups: 16/16. Vanilla and Immortals each passed in EN, RU,
  DE, FR, CS and UK; two promoted beta and two core-disabled cases also passed.
- Every enabled-patch startup logged `LAIR_WOUNDED r3`, R2 transfer readiness
  and successful patch installation. This includes stored defender preference
  off, with no optional defender package installed.
- All three pre-existing game profile files were restored and hash-verified.
  The installed Steam game directory was not used as a test target.

## Server

Applied `20261013000000_pure_extended_stable.sql` to production and ran a
separate readback. Stable Vanilla, stable Immortals and legacy beta LR0 codes
are accepted. Missing-core and data-only combinations remain rejected. The
private schema remains inaccessible to anonymous clients. No RPC permissions
were changed.

Validator definition MD5 changed from `f76f8b561b2c84714a1266af96316db5` to
`76141b01c3f2a832b43ad34765b4f855`.

## Artifact scope

The 26 repackaged static payloads are byte-identical to the accepted Pure beta;
the existing color package keeps its published identity. New native runtime:
`1.3.72-pure.14`. Arcane packages and local experimental work are excluded.
Publication tooling verifies assets and source tags before signing catalogs,
then checks public catalogs against the pushed commit.

Startup checks confirm native main-menu readiness. They are not a new full
multiplayer-match acceptance run.
