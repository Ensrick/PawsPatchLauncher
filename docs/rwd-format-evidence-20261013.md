# RWD format and loader evidence, 2026-10-13

This addresses the documentation findings in
[Ensrick's review of PR #8 at 313ee13](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/8#pullrequestreview-5501117737).
It does not change archive serialization, stamps, compatibility rules or the published launcher.
The preceding [relocated-path fix](rwd-relocated-paths-20261013.md) remains local.

## New local measurements

Read the retail archives from the Steam installation whose 1.3.72 EXE SHA-256 is
`1eb79bbb678668be5a05f8c98103cd9988048490ce7c53c74c9a1d0813e9cd45`.
Built the current RwdTool from the working tree based on `93c2f2f` with the local relocated-path changes.
Build: zero errors and zero warnings. No game process was started and no installed game file was modified.

An independent Python `struct`/`mmap` reader measured the section table, footer and SHA-256 of each payload.
Local evidence is in workspace `outputs/rwd-doc-audit-20261013/measure.py` and `measurements.json`.
Extracted music and the repacked archive stay outside the repository; no game payload is included here.

| Measurement | Data.rwd | Music.rwd |
|---|---:|---:|
| Archive bytes | 386,300,748 | 86,960,244 |
| Entries | 10,526 | 31 |
| Payload bytes | 385,157,324 | 86,957,244 |
| Names containing uppercase characters | 10,522 | 31 |
| Nonzero unknown per-entry fields | 0 | 0 |
| Zero-length entries | 1 | 0 |
| Entries contiguous in load-index order | 10,526 | 31 |
| Load indices are a permutation of 0..n-1 | Yes | Yes |

In index order, every entry's offset equals the preceding payload's end (starting at zero), and the final end
equals the size of `Files`. This supports the load-index interpretation without relying on footer order.
Data's empty entry and the following nonempty entry share one offset: sorting only by offset does not determine
their relative order. A check that treats their footer order as the tie-breaker would wrongly report two mismatches.
These observations are not a new rejection rule for other archives. CLI `verify` currently warns only when
indices are not a permutation; it does not validate their relationship to payload offsets.

Retail section-table filler pairs are `0x002ff4a8, 0x0012fe60` in Data and `0x002fb630, 0x0012fe60` in Music.
They resemble memory addresses, as Ensrick suggested, but that interpretation is unproven. The writer emits zeros.

### Music extract/repack with the current writer

Ran `extract Music.rwd <separate-output>/music-input`, then
`pack <separate-output>/music-input <separate-output>/music-repacked.rwd --stamp 89317623`.

- All 31 payloads have identical sizes and SHA-256 hashes.
- Archive size, section offsets/sizes, the header and the complete `Files` section remain identical.
- All 31 stored names change to lower case, footer entries change order, and each section-table filler pair is zero.
- Per-file payload offsets and load indices are unchanged.

The old README's claim that only footer order and filler change described the earlier case-preserving writer.
The current writer also changes stored name case. Equal payload bytes do not establish that the audio loader
will resolve every spelling of those names; this measurement did not play the repacked Music archive in game.

SHA-256 for identifying the exact inputs and output:

| Archive | SHA-256 |
|---|---|
| Retail Data.rwd | `e60642ea999457015997e58e38d2305b409daee9925d4eca88f1d50ef9655b6e` |
| Retail Music.rwd | `5f9d29df9d3f368387403a4a0720053e8f1a999a98c272981125f0d2ccf52829` |
| Repacked Music.rwd | `a8776329a4d07326a66f3246b8d15c6a5c713008e74687af728835deb8d70d98` |

## Previously reported in-game evidence

These checks were not rerun during this documentation follow-up.

| Source and path | What was observed | Scope |
|---|---|---|
| Ensrick, `+Exec` script lookup | Lower-case archive name found by a mixed-case request, and mixed-case archive name found by a lower-case request | Two directions on one script path; textures/audio untested |
| Ensrick, startup `addmod` | Last archive wins over an earlier archive and `<game>/data`; `Documents/Kohan2/data` wins over those sources | Script probe, isolated copy/profile per case |
| Ensrick, follow-up control | `<game>/data` alone supplies the script; a second full run reproduces all five cases | [Published control results](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/8#issuecomment-6142421272); probe reported in PR #9 |
| Our launcher `adddepot` tests | Reversing archives changes the menu label; archive/loose-directory reversal follows mount order; generated mounts and disabling work | [Six integration cases](custom-rwd-mods-local.md#evidence), menu-label resources |

The review reports game loading with zero section filler. This establishes acceptance for the tested archives,
not the original fields' purpose. The script `addmod` evidence must not be substituted for the launcher's
`adddepot` tests, nor for untested texture/audio loaders or multiplayer simulation equivalence.

## Follow-up status

- Corrected the README's format evidence, current integration status and remaining limitations.
- Corrected `ReadGameId`'s comment: the helper decodes an id; `Open` rejects ids other than `K2`.
- No CLI or launcher behavior changed in this documentation step. The earlier 154 focused checks and 37,542
  full-suite checks belong to the relocated-path implementation, not a new full test run here.
- Next: inspect current PR #1/#8 contribution status, then assess the separate recovery PR #9 and its probes.
  The Documents-depot report remains deferred, with no new localization/keybind admission restrictions.
