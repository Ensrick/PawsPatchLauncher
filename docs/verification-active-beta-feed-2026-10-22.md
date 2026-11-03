# Repair missing widescreen betas in the active catalog

## Root cause

The 2026-10-20 widescreen publication updated feed/v2/beta.json through the
legacy FEEDS mapping in PrepareRelease081.py. Current launcher configuration
points to feed/v3/beta.json, which still advertised Arcane Wars 0.4.2-beta.3
and Vanilla/Immortals 0.4.1-beta.5. The later 1.0.2/1.0.3 launcher releases
correctly preserved game package data, so they also preserved this stale v3 data.
The original four-catalog readback therefore did not prove availability to
current launchers. The user correctly reported that no patch update appeared.

## Repair and validation

Only the active v3 beta catalog is changed: seven already-published patch package
records, corresponding patch guide versions, and the two missing changelog entries.
The Arcane Wars source recipe and all unrelated packages are byte-equivalent as
JSON objects. The other five catalogs are unchanged. No new launcher binary or
new game archives are required.

All seven public ZIPs were downloaded, hashed against the signed v2 records and
validated against their package manifests before signing the repaired v3 catalog.
Targets: Arcane Wars 0.4.2-beta.4; Vanilla/Immortals 0.4.1-beta.6; pure runtime
1.3.72-pure.15-beta.6.

New regression checks use the actual beta URL from launcher.config.json and the
launcher's signature verifier, package selector, displayed-version resolver and
UpdateDetector. Before the repair they fail with the stale pawpatch-core version.
After repair, 64 checks pass, including upgrade detection for all three mods in
six text languages. The old catalog reproduces no update in those same cases.

The normal test suite now checks active beta patch parity. The original widescreen
publisher's readback also refuses success when a configured active feed still
advertises stale patch records, even if its legacy/v2 catalog and ZIPs are correct.

Evidence: task workspace outputs/active-beta-repair-20261022, including baseline
catalogs, before.log, after.log, public asset copies, preparation.json, tests.log
and public readback after push. Existing installation/rollback acceptance applies
to these identical ZIPs; no new game or VM session was launched for this feed repair.
