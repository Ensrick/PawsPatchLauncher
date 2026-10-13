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
- Full integrated multiplayer validation is pending; `PrepareRecoveryBeta.py`
  requires a recorded successful result before signed-feed promotion.

Earlier private r2 PC/VM recovery results do not substitute for testing this
different shipping composition. `IntegratedVmRig.cs` uses the real packaged
helper; `rig_input.c` supplies only fixed test menu commands and Direct IP input.
Neither test component implements recovery or alters transfer behavior.

Evidence directory: workspace `outputs/release-recovery-20261013/`.
