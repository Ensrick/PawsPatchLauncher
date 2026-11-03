# Multiplayer save and host-departure regression, 2026-10-22

## Scope

The user authorized independent live checks. Runs use the isolated stock Vanilla
1.3.72 fixture in `D:/PawsMultiplayerRig`, with the production revision 2
multiplayer-saves module. Only one host/client pair runs at a time, with fresh
private profiles, retained evidence and the rig's owned-process cleanup.
Steam was running; no ordinary game was running when the suite started.
No VM or recording was started. Free physical memory during the checks was about
18.5 GiB; available commit was about 38.4 GiB.

This is a one-PC loopback baseline, not a test of all modules in the published
beta, all three mods, two physical computers, packet loss, or direct hot-join.
The autosave interval is shortened to one minute for this baseline. Renderer
assertions and the network enum assertion remain enabled.

## Completed save checks

- Autosave: 12/12 checks passed. Host autosaves A/B completed and the client
  wrote both copies without independently autosaving. Both processes exited
  normally without assertion, desync, unhandled exception or mutex timeout.
- LoadCopy: 12/12 checks passed using the new client-side B copy. Both peers
  entered the loaded match. The host autosaved; the received-save client refused
  to create a new copy because its source header was not loaded. The original
  host left, the client took over, and both processes exited normally.
- An additional retained-log check confirms the replacement host actually
  reached its autosave deadline and refused the unsafe save. Merely seeing no
  save before its deadline would not prove this guard ran.
- The transferred file is 224,169 bytes, identical at both endpoints and to the
  input client copy; SHA-256
  `7DE50A888A0A30F70DD795B7B61583401F3A4FC7961F525781A9B32917EB960B`.
  The source file was not modified. Save-load success and clean logs do not
  establish exhaustive equality of every simulation field.

Evidence directories:

- `D:/PawsMultiplayerRig/runs/20261022-211004-418-Autosave-0fed33`
- `D:/PawsMultiplayerRig/runs/20261022-211602-081-LoadCopy-765572`
- Task workspace `outputs/multiplayer-regression-20261022` contains the suite
  driver, summary and the additional replacement-host assertion.

## Test-report correction

LoadCopy previously checked the unsafe received-save path only if it happened
to observe a host autosave. A run that never exercised that path could therefore
pass. The host's completed autosave and the client's explicit source-header
refusal are now mandatory checks. A further mandatory check requires the
replacement host's explicit unsafe-autosave refusal. The new conditions were
verified against the fresh live run's retained logs.

## HostLeaves series

The first fresh run passed all six checks:
`D:/PawsMultiplayerRig/runs/20261022-212418-084-HostLeaves-6a0f4f`.

The second run reproduced Ensrick's assertion and timed out:
`D:/PawsMultiplayerRig/runs/20261022-212903-897-HostLeaves-985a96`.
The host log proves RigClient joined and the host exited normally. The client
displayed `TG Assert`, `Serialize.h:395`, `_data<_n_possibilities`. No Ignore,
Retry or Abort button was pressed. The rig's timeout stopped its owned client
and removed its temporary files. The third planned repeat was not started.
The run is recorded as failed, not as successful because the process stayed alive.

A read-only process inspection and local full-memory dump were captured while
the assertion dialog was open. Dump analysis verifies the failing call bytes,
stack arguments and current request descriptor:

| Item | Observed value |
| --- | --- |
| Enum reader RVA | `0x6AD25` |
| Assertion call RVA | `0x6ADC5` |
| Enum-reader caller return RVA | `0x148DAE` |
| Type-specific decoder RVA | `0x148D9C` |
| Request handler RVA | `0x1556B3` |
| Request descriptor | `TGC_UpdateTaskStatusOrderRequest`, wire ID 294 |
| Decoded value / exclusive limit | `5 / 5`, valid range `0..4` |
| Session / ownership | Lobby (state 1); client has become host |

Validated return-address chain (RVAs): `148DAE -> 1556D6 -> 15DB92 -> 14E811 ->
14E750 -> 14CC68 -> 15B4F1 -> 3056A7`. Traversal stops when the frame leaves
the captured thread's stack; incidental words resembling pointers are excluded.

This is a real invalid task-status read, not the synthetic registry-index case
in the previous audit. It identifies the failing request and takeover state,
but does not yet establish why value 5 was emitted/decoded, whether a stale
request or stream alignment is responsible, or that accepting it is safe.
The global assertion bypass remains unsuitable. Investigate the sender and
task-status request/decree handling before changing production behavior.

The dump is local only (568,526,982 bytes); it is not added to Git or uploaded.
SHA-256: `6d4da1141fdd4c252f15ff493c23aa69cfaa6cfd0947cebefef3dd1cb0e0f85a`.
The task output's `assert-capture/verified-summary.json` contains distilled
evidence, with raw thread snapshots and disassembly alongside it.
All 21 fixture files matched their preparation hashes. No test game remains.
Distilled findings were [posted to Ensrick's PR #7](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/7#issuecomment-6349690941).
Final evidence verification counted three successful runs (30 checks plus the
explicit replacement-host guard check), one reproduced assertion/timeout, and
one cancelled planned repetition. The suite is not reported as fully passed.

## Remaining gates

Use the captured request to build a narrowly scoped candidate and rerun the
failing takeover scenario before proceeding to complete-patch and two-computer
checks, including different local language/key settings. No unconditional
assertion bypass, simulation hash reset, production package or update catalog
is changed by these tests.
