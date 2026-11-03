# Launcher 1.0.1 validation

Scope: Ensrick PR #19, exact head `d9f7477fa87c30163da7713e15623eeec434888a`,
imported with original authorship as `382a45e`. No other proposal is included.

- Known Steam 1.3.68 and 1.3.72 executables select separate hash-verified lobby
  defaults. Unknown executables are left untouched.
- Repair keeps existing matching rules and backups; only affected launch chunks
  and their map-generator cache are replaced. General preferences, saves,
  localization, voice, keybindings and multiplayer admission are unchanged.
- Added a private Documents-root entry point and private launch delegates so the
  real Play controller can be exercised without accessing the user's game data.
  Normal launches still use the same production repair and Process.Start calls.
- Release build: no warnings or errors.
- Full test suite: PASS 38105, including 753 lobby-preference checks across both
  build defaults, three mod modes, patch/data-only combinations, both lobby
  chunks, malformed input, original backups and repeat/unknown-build behavior.

## Native fixture and retained failures

Evidence: `outputs/lobby-repair-101` in the task workspace.
Original 1.3.68 EXE SHA256:
`CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A`.
Data.rwd SHA256:
`E60642EA999457015997E58E38D2305B409DAEE9925D4ECA88F1D50EF9655B6E`.

The fixture uses a separate game folder and Documents redirected by the existing
rig shim. No VM, real Documents changes or recording. Preparation on D: stopped
for insufficient disk space before any game launch. Verified copied files were
removed individually; testing moved to G:.

Initial fixture setup omitted the stock loose options_dialog.tgi and failed at
menu initialization. Restoring the existing pinned stock file resolved it.
An initial script used Run instead of Exec and timed out. The corrected seed
run reached the 1.3.68 main menu and exited normally (`log-3-ok.log`).
Two subsequent match scripts timed out waiting for the game state at the
single-player loading screen. Their logs/dumps are retained; they are not passes.

The Play-path fixture marks the game's own fresh SingleLaunchSettings with a
serialized kingdom16 marker. This is a synthetic incompatible-lobby trigger,
not the contributor's original crashing preferences. Both Documents and local
Userdata files are checked before native launch for correct 1.3.68 defaults,
byte-identical original backups and preservation of unrelated chunks.

## Native acceptance

`launch-test-r3.log`: LOBBY LAUNCH PASS. The actual launcher Play controller
repaired both locations before starting the original 1.3.68 process. The native
game loaded a Vanilla skirmish, entered gameplay after the loading screen's
Continue button was clicked, and exited normally (`log-6-ok.log`, exit 0).
The final game log has no ERROR or ASSERT lines. Repeating the preflight after
exit left clean preferences and the number of backups unchanged.

The earlier WaitState timeouts were the loading screen awaiting that Continue
action; they were not successful runs. The final check is assisted UI acceptance,
not a wholly unattended match test. The gameplay view was inspected directly.

Reproduction: prepare an isolated game with the exact EXE above, stock runtime
files including the loose options_dialog.tgi, the rig's shfolder.dll and a fresh
private Documents/Kohan2/data/User profile. Seed Preferences.rup with a normal
menu launch/exit. Add `lobby-fixture.marker` at the fixture root and
`game/startup/lobby-match.txt` with RequestSinglePlayer, RequestCustom,
SelectLaunchType RANDOM_MAP, StagingLaunch, sufficient Wait time to click
Continue, WaitState game, a short Wait, Echo LOBBY_REPAIR_MATCH_PASS, ReallyQuit.
Run PreviewRenderer with `--smoke-test --lobby-launch-fixture=<root>` and an
output PNG argument; click Continue in that test window when loading completes.
The test never uses or resets the player's real settings.

Final tagged artifacts and six signed-catalog readback are checked separately
after CI publication. Game-package versions and payloads are unchanged.

## Public artifacts

- Source/tag: `a31bdc015bcc0325e39904dc7d56252bc11d238d`, `v1.0.1`.
- GitHub build/test `39273074842` and publish `39273079440`: success.
- Public EXE and ZIP downloaded and verified against the tagged artifact manifest.
  EXE: 74,311,541 bytes; SHA256
  `2D80061431E37824F77A763B97D044C7E77634BB50CD7886C7BC558CB058197E`.
- Both window-start checks of the downloaded EXE passed, including standalone use.
- All six signed catalogs are updated only in launcher metadata/news; game
  package data is verified unchanged. Final remote readback is retained in
  `outputs/lobby-repair-101/release-feeds/readback.json` in the task workspace.
