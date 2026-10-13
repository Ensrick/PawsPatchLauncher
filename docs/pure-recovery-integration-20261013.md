# Pure recovery integration, 2026-10-13

Target: Vanilla / Immortals 0.4.1-beta.1, native helper 1.3.72-pure.15-beta.1.

The shipping adapter is embedded in the existing lobby compatibility module.
The regular helper owns R2 transfer and the missing-client guard; recovery does
not install duplicate copies. It retains the existing QPC import target (including
compatibility/overlay wrappers), executes on the verified game window thread,
and polls at most every 50 ms. Three native call sites and their surrounding
instructions are checked against the hash-bound 1.3.72 image.

Recovery completes the native save task before leaving the match, then selects
the saved game in the lobby. The public build keeps native disconnect voting
manual. Losing the original host saves and pauses on the replacement host, with
manual lobby reload. Ordinary exits are not always distinguishable from drops.
This is save/lobby recovery, not direct entry into a running simulation.

The diagnostic archive collects `Logs/paws_recovery.log`. The runtime package
includes Ensrick's unchanged MIT notice. Production does not require the private
test launcher, profile shim or input plugin.

## Validation

- 122 recovery native assertions and 20 shipping-adapter assertions.
- 30 relocated code guard definitions; R2-owned instructions excluded only from
  the recovery adapter's redundant validation.
- Existing native compositions: 804 installs across 12 compositions and 3 bases;
  random-map transaction checks: 477.
- 16 installation/rollback cases and 16 actual menu launches: Vanilla and
  Immortals, EN/RU/DE/FR/CS/UK, stable rollback and patch disabled.
- The installed adapter and R2 readiness are required by the startup oracle.
- All four native helper preflights accept an installed custom RWD module.
- Integrated two-peer LAN recovery passed for both Vanilla and Immortals:
  initial match, terminated client, native save completion, host lobby return,
  restarted client, save transfer, and both peers resuming the saved match.
  Both remained running for the 20-second final observation; no native
  out-of-sync, assertion or unhandled-exception markers were found in the logs.
- Transferred saves matched byte-for-byte: Vanilla 221,396 bytes, SHA-256
  `98421ff9bcdac425479a14fbf71eeb218a4115ae5d66d6eb3c75cbb30dd56f76`;
  Immortals 222,844 bytes, SHA-256
  `26e7791f4cd48d6d27ff9a904f839b46672b5426becddcf878d9760e1ef26dc0`.
- All four installed helper hashes were checked against the release build in
  both test installations. `release-validation.json` records these hashes and
  gates signed-feed promotion. Both test games were stopped after collection.

Earlier private r2 PC/VM recovery results do not substitute for testing this
different shipping composition. The current integrated tests used two isolated
local processes. PC/VM could not be rerun because guest UI control was unavailable.
`InstalledHarness.cs` invokes the unchanged packaged helper's preflight and
composition methods while allowing two owned local game instances. Normal helper
startup remains covered by the 16 separate menu launches. `rig_input.c` supplies
only test menu commands and Direct IP input; neither test component implements
recovery or alters transfer behavior. The shipping build has automatic timeout
voting disabled; the test script issues one ordinary native disconnect vote.

This confirms the tested save/lobby path, not Internet/Steam recovery acceptance,
long-term deterministic synchronization, seamless hot join or automatic host
migration. The earlier original-host-loss result remains a private-r2 test.

Evidence directory: workspace `outputs/release-recovery-20261013/`.
