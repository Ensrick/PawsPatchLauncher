# Pure 0.4.1-beta.2 installed-build acceptance — 2026-10-13

Target: Vanilla and Immortals beta, runtime `1.3.72-pure.15-beta.2`.
The four packaged helpers contain Ensrick's multiplayer saves module from PR #9,
paired with the coordinated recovery adapter and the save-source guard from PR #11.
The launcher remains 0.9.3. No production database migration is required.

## Integration fixes

- Wait for the game's delayed PeekMessage import to resolve before installing the saves module. The installer
  checks the matching user32 image and its exports; native byte guards remain mandatory.
- Include save copies and the five-minute autosave in the compiled Pure recovery beta. There is no extra
  selectable feed component, and stable helpers do not include this module.
- Confirm a fast autosave that was created and detached between the 50 ms polls by comparing its output with
  metadata captured before dispatch. The output must have changed, be nonempty and open without sharing writes.
  Unknown baselines, unchanged files, empty files and active writers cannot acknowledge completion. This releases
  the coordination flag; it does not validate save contents or provide a mutex for all manually queued commands.
- PMS2 supplies the actual save-data directory. Prefixed diagnostics use the existing `Logs/paws_recovery.log`,
  which the launcher already collects. Log truncation and failed checksum reads are bounded, and a failed engine
  string allocation cannot reach the save-copy string release.

## Real-game checks

Two isolated local LAN processes were used for each mod, with the unchanged packaged helper composition.
The game was 1.3.72, SHA-256 `1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45`.
Each copy had a private Documents folder, 800×600 windowed settings, muted audio and a 30 FPS limit.
No virtual machine or screen recording was used. QA commands went through the game's script API;
session state and elapsed simulation time were observed with read-only process access.

Both Vanilla and Immortals passed:

1. A manual host save produces a nonempty client copy.
2. Autosave A and B run at the shipping interval. Both measured intervals were exactly 300 seconds in each mod;
   each host save produced a client copy, and the client never originated an autosave.
3. Client process loss causes a completed recovery save and automatic return to the lobby.
4. The restarted client rejoins, receives a byte-identical recovery file, and both peers load and advance the
   simulation. Paired elapsed-time readings remain within two seconds.
5. Loss of the original host after that network save load promotes the client. The source guard rejects the
   unsafe new save; the replacement host survives and advances the simulation for another 70 seconds.
6. A client copy can be hosted as a new match, transferred to another client and played. Its host can save again;
   the received-save client safely declines a copy when its source header is unavailable.
7. No assertion, desync, unhandled exception or mutex timeout was found in these runs.

The fast-completion fallback was exercised by Vanilla's B slot and Immortals' A slot.
Vanilla's first runner finished A/B and lobby recovery but then incorrectly required backup mode after the
immediate socket-close event had already completed recovery. That failed runner result is retained. The oracle
now accepts immediate recovery or the native backup/vote path, and the complete recovery/load-copy sequence was
rerun with the same helper hashes. Its `--slots 0` result is not used as autosave evidence.

## Other validation

- 16 actual main-menu cases: both mods in six languages, plus stable rollback and patch disabled. The 12 beta
  cases were rerun for the final helpers; four unchanged stable/disabled observations were reused.
- All 16 installation/rollback/preflight cases were rerun for the final signed test feed.
- 37,542 launcher assertions and 12,493 isolated database assertions passed.
- 133 native recovery, 24 integrated recovery and 103 coordination checks passed. The coordination suite was
  rerun with the final saves DLL; 19 reverse-direction checks used the real recovery DLLs.
- 79 standalone saves checks, 105 including the pinned image (includes those 79), 25 real-file/writer-lock
  checks and 14 PMS2 installer checks passed.
- The four helper builds passed 804 guarded composition installs, 477 random-map transactions, 24 lobby checks,
  and 145 AI-option checks for each policy profile. The 80 checked bytes of native `Block` remain unchanged in
  all 12 compositions. No assertion-skipping change from PR #7 is included.

Local evidence is under workspace `outputs/release-saves-20261013/`: `release-validation.json`, `network/`,
`matrix/`, native/build logs and `publication-final/`. The collector checks all installed helper hashes against
the packaged binaries and keeps the failed runner record alongside the successful rerun.

Recovery is still save → lobby → rejoin → load. This acceptance does not establish hot join or PC/VM behavior.
Stable and Arcane Wars catalogs, other packages, launcher release identity and production server state are outside
this release's scope. PR #12's separate developer rig received review comments and is not merged by this release.
