# Launcher 1.0.2 publication

- Source/tag: `4859a8d4f35f02909aa7320aea9e4244cc1b1751`, `v1.0.2`.
- GitHub build/test run `39628123931`: success.
- GitHub release run `39628127599`: success, including tests and packaging checks.
- Published EXE and ZIP downloaded and checked against the tagged artifact
  manifest. EXE size: **74,347,951 bytes**; SHA256:
  `6781934263F7A1AF4C325BBD1946F57DF33452828E3196F573244026FB3764C0`.
- Downloaded EXE passed two window-start checks, including standalone use.
- Six signed catalogs promoted and read back at
  `8aec02262825d58ea47a46ac61aba646ddde9149`; game package data unchanged.
- Evidence: task workspace `outputs/launcher-102-20261021/release-feeds/`
  (`preparation.json`, `readback.json`, artifact manifest, EXE and ZIP), and
  `outputs/launcher-102-20261021/smoke.log`.

Scope: Ensrick's PR #18 FAT/exFAT attribute-query fallback only. Local test
limits are in [the RWD report](verification-rwd-fat-exfat-2026-10-21.md).
The custom-mod manager candidate from #17 is not part of this release.
