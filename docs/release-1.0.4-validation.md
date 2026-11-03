# Launcher 1.0.4 validation

Publication authorized by the user on 2026-10-26. Source is isolated from the
working tree containing the deferred custom-mod page and delta prototype.
Only background service dispatch, progress coalescing and the ModDB dispatcher
bridge are included, together with an isolated WPF regression fixture.

Previous local accepted-UI candidate: 13 background installation checks passed,
including extraction of 256 files, apply/uninstall/rollback, cancellation,
worker-originated ModDB dialog, and coalescing of 100,000 progress reports.
The original five UI layout/implementation files match the baseline after
line-ending normalization. No real game or VM was run.

Exact release-source test results and public asset/catalog verification are
recorded below after their completion.

## Exact release source

- Console suite: 38,182 checks passed. This excludes unrelated local tests.
- WPF background fixture: 13 passed; 295 input-priority timer ticks, maximum sampled gap 30.3 ms. This is a fixture measurement, not a general latency guarantee.
- Both suites used the isolated release checkout with the original accepted UI.
- No game, VM or recording was started.

## Published artifact and feed validation

- Source/tag: `9f1fa4a5acbd9ef45172c682db74f823ab05d27d`, `v1.0.4`.
- GitHub build/test `40273912557` and publication `40273913742`: success.
- Public EXE/ZIP downloaded and verified against the tagged artifact manifest.
- EXE: 74,353,927 bytes; SHA256 `BF411A7358A832E768BEA0B5B40D5B56F5E4D25D6020BED5F46CECAC7AE1D1FA`.
- Both window-start smoke checks passed, including standalone use.
- Six catalogs staged with valid signatures and unchanged game packages.
- Evidence: workspace `outputs/background-install-20261026/release-feeds/`,
  `release-tests.log`, `release-ui.log`, and `release-smoke.log`.
- Final public catalog readback is stored in `release-feeds/readback.json`
  after the catalog commit is pushed.
