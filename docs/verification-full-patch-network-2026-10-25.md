# Full-patch network regression, 2026-10-25

Tests use the public beta payloads published on October 23. A fresh read of the
configured v3 catalog on October 25 still lists Vanilla/Immortals
`0.4.1-beta.7`, Pure runtime `1.3.72-pure.15-beta.7`, and Arcane Wars
`0.4.2-beta.5`. Production payloads and feeds are not changed by this work.

## Isolation and reproducibility

- Source checkout: `work/recovery-beta-release`, base `2f81edb`.
- Evidence: workspace `outputs/full-patch-network-20261025`.
- Private peers and saves: `G:/PawNetworkQA-20261025`.
- Installation uses PatchMatrixRunner with the signed release feeds and verified
  package cache from `outputs/publish-task-status-20261023`.
- One pair of local LAN processes at a time, 800x600 windows, 30 FPS, private
  Documents. No VM, screen recording, or modification of the Steam installation.
- Pure runs call the unchanged shipped helper's composition methods through
  InstalledHarness. This skips only the public one-game launch restriction.
- Arcane's public helper has a monolithic entry point. Its QA build adapts
  process discovery to the private game path and redirects managed preferences
  into the fixture. All release inputs are hash-checked, embedded native payloads
  retained, and reported feature sets compared. It is **not byte-identical** to
  the public helper; `qa-manifest.json` records both hashes and adapted sources.
- The QA input plugin sends ordinary engine script commands on the main thread.
  It does not implement saves/recovery or alter simulation/session state.
- New runs abort if available RAM falls below 3 GiB or commit headroom below
  5 GiB. Process termination is restricted by image path and creation time.

The first preparation attempt hit Windows path length limits while copying the
installer's historical caches. The runner now copies active game files and
installation state, omits installer rollback/cache history, and uses short paths.
The failed preparation is not counted as a game regression. Automatic approval
review rejected cleanup of its temporary copy, so that copy remains on disk.

## Results

Vanilla (`vanilla-r2/result.json`): **16 checks passed**. Two peers ran with the
complete standard Pure patch and stock desync handling. Manual saves and client
copies worked. Autosave A and its client copy completed at the shipping five
minute interval. After a forced client exit, the host saved, returned to the
lobby, transferred a byte-identical resume save, and both peers resumed advancing
simulation. After the original host was then terminated, the new host survived
for 70 seconds and refused the unsupported received-save source. A separate pair
successfully hosted, transferred and loaded the earlier client copy. No assertion,
out-of-sync, unhandled exception or mutex-timeout entries were found.

Immortals (`immortals-mixed-r1/result.json`): **23 checks passed**. English host
and Russian client have identical gameplay options. Their network identities
match, different local pause bindings (F12/F11) were executed, and both entered
a live match. The five-minute autosave, manual/client saves, **two consecutive
client crash recovery cycles without restarting the host**, received-save
host-loss guard, and separate hosting/loading of a client copy all passed.
Both resumed games advanced on both peers; transferred resume saves matched
byte-for-byte. The final native error scan was empty.

Arcane Wars (`arcane-r1/result.json`): **11 checks passed**, using the documented
QA adaptation. Two peers entered and advanced a match, the host saved, and it
continued simulation after a forced client exit. In a fresh lobby the original
host exited normally; the client took over with valid TaskStatus, launched a new
match, advanced simulation and saved. Both surviving processes exited normally;
the native error scan was empty. Its published composition has lobby compatibility
and fast transfer, but does **not** include Pure's multiplayer autosave/recovery
module. Do not describe this as automated Arcane match recovery.

Offline language identity checks on all eight unchanged public Arcane helpers:
**48 passed**, covering five localization modules per helper and rejection of a
different AI component selection.

Final aggregation: **50 live checks and 48 offline identity checks**. Package
versions and hashes in the fresh public catalog match the tested signed release
inputs. The stock Steam executable still has its expected SHA-256. All 234
personal city-preference files retained their pre-Arcane hashes.

## New finding: diagnostic worker lifecycle

The game checks passed, but three `PawsGraphicsRecorder.exe` workers remained
after normal Arcane exits. Their target games and helpers had already exited.
Each worker retained one waiting thread and about 8–9 MiB working set; directory
metadata reported zero-length diagnostic logs. Follow-up reads found valid
START/ATTACHED/ARMED records: the open-file length was stale, not missing logs.
This initial snapshot did not establish the cause of the hang.
The same recorder binary successfully attached and exited in the Pure tests.

Retained evidence: `orphan-diagnostics.json`, a 20,322-byte
`diagnostic-worker-hang.dmp`, and `diagnostic-worker-stack.json`. PID, image path
and start time were checked before stopping those three private-fixture workers.
No unrelated diagnostic process was stopped. Final inspection found no remaining
test games, harnesses or recorders. Root cause and a bounded recorder startup/exit
fix were a separate next step; this original network run did not pass diagnostics
lifecycle. The subsequent local repair and its separate verification are recorded
in [the recorder lifecycle report](verification-recorder-lifecycle-2026-10-25.md).

## Limits

These are local loopback tests, not two physical PCs or a latency/loss campaign.
They do not prove direct rejoining of a running match or repair an actual desync.
Declining to save after takeover of a received save is an existing safety limit,
not successful host-loss recovery. No beta or launcher publication is included.
