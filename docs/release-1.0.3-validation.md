# Launcher 1.0.3 validation

The user authorized agent verification and publication on 2026-10-22.

- Final build: zero warnings/errors.
- Full launcher suite: 38,188 checks passed (includes the pre-existing local Arcane source tests; CI verifies the exact committed release independently).
- WPF UI checks: 81 per language, 486 total across ru/en/de/fr/cs/uk. The final run raises Click on the actual replacement button, checks the tooltip path, and exercises cancellation, invalid replacement, Undo, apply/reopen, overlap changes, ordering, game-running guards, capacity and replacement at capacity.
- All six final renders visually inspected at a 1050x680 launcher test-window size: no text/button clipping in the tested dialog.
- No game or VM was launched. Installer operations use disposable fixtures.
- Local evidence: task workspace outputs/launcher-103-20261022, build-final.log, tests.log, ui-final-LANGUAGE.log and custom-mods-enhanced-LANGUAGE.png. Earlier UI logs are not final evidence: an initial build hit a locked preview DLL; after it exited, the build and all six UI runs were repeated successfully in sequence.

Release EXE/ZIP verification, standalone window-start smoke checks and public signed-feed readback are recorded after publication.

## Public release verification

- Source/tag: `5de31e615090184da2f30ceaf2b13c574f575c97`, `v1.0.3`.
- GitHub build/test `39813662198` and publication `39813666882`: success.
- Public EXE and ZIP downloaded and verified against the tagged artifact manifest. EXE: 74,314,899 bytes; SHA256 `AAF9ECBC6CF91B2749FC1FE80992B9F56DEDCD6C3B748B675C2A1B44C2814414`.
- Downloaded EXE passed both window-start checks, including standalone use.
- Six catalogs were staged and signatures verified with unchanged game package data; public readback is retained in `outputs/launcher-103-20261022/release-feeds/readback.json` after push.
