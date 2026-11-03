# Host takeover: initialize the local task status, 2026-10-23

## Root cause and narrow correction

The Oct 22 dump's `TGC_UpdateTaskStatusOrderRequest` failure was traced back to
the producer, rather than worked around at the shared enum reader. In the same
dump, `TGC_AdminNode` at `0x1662a044` contains `0x73756a2d` at offset `0xa0`.
`UpdateTaskStatus` (RVA `0x14af6e`) reads this field and passes it to the request
writer. The writer at `0x148d89` serializes only three bits for five possible
values, without validating its input. The low three bits of that captured word
are **5**, matching the actual assertion.

The native constructor at `0x14954c` does not initialize this field. Its base
constructor/reset initializes the separate per-player status at `+0x38`, not
the local status at `+0xa0`. The successful/failed world-task callbacks write
valid values later, but a lobby can change host before ever loading a world.
This explains why restarting the same scenario is intermittent and why simply
accepting decoded value 5 is not a repair.

`TaskStatusPatch` hooks only the successful constructor epilogue at `0x149760`:
set `AdminNode+0xa0` to 0 (idle), replay `mov ecx,[ebp-0xc]; mov eax,edi`, and
continue the original epilogue. No registers, flags, other object fields, wire
format, resource pools, renderer checks or enum-reader assertions change.

The helper may install after the initial AdminNode was constructed. While all
game threads are paused it also initializes that existing object's field, but
only after checking the native vtable, session state 0, no server and no player
list. Active-session attachment is rejected. The installer verifies code bytes
before writing, reads back writes, rolls back partial failures and retains
allocated code if rollback cannot be verified. It never clamps a received value.

## Offline and build verification

- 417 installer checks: three ASLR bases, existing/absent initial object,
  redirected call targets, active-session rejection, mismatched code rejection,
  partial writes and rollback at every fallible operation.
  Persistent partial-write failures retain the allocated target rather than
  free memory which an uncertain redirect could still reach.
- 94 native-instruction cases against the hash-pinned 1.3.72 snapshot: original
  and corrected constructors with 11 prior memory patterns; actual status
  producer, writer and reader at every bit alignment, including crossing byte
  boundaries. Only ancillary constructors, allocation and logging are stubbed.
  The candidate leaves every other object byte, register and flags unchanged.
  Invalid wire values 5/6/7 still reach the original assertion.
- Four Vanilla/Immortals helper variants compiled. Twelve native compositions
  at three image bases passed (828 hook installs); received-save Block bytes
  remain unchanged. Existing random-map transaction checks passed (477).
- Eight Arcane helper variants compiled and passed their five existing offline
  self-tests each. This is build/composition evidence, not a live match in every
  mod or a public release.

Evidence: workspace `outputs/multiplayer-regression-20261023`, including native
test results, candidate builds and retained series results. Candidate builds
are local only; their existing beta version labels do not announce a release.

## Live regression: six runs, 63 checks passed

The isolated fixture is `D:/PawsMultiplayerRig`: stock 1.3.72 Vanilla, unchanged
production multiplayer-saves r2 plus the new initialization fix. One host/client
pair on this PC at a time, fresh private profiles, one-minute test autosaves,
owned-process cleanup, no VM and no recording.

All runs are retained under `D:/PawsMultiplayerRig/runs/`:

| Run directory | Scenario | Passed checks |
| --- | --- | --- |
| `20261023-190825-task-status-hostleave-1` | HostLeaves | 8/8 |
| `20261023-191337-780-HostLeaves-5e7a7b` | HostLeaves | 8/8 |
| `20261023-191823-909-HostLeaves-f887f1` | HostLeaves | 8/8 |
| `20261023-192309-453-Autosave-0ddcb8` | Autosave | 14/14 |
| `20261023-192836-927-LoadCopy-529c98` | LoadCopy | 15/15 |
| `20261023-193720-task-status-launch` | HostLeavesLaunch | 10/10 |

The first client's pre-initialization word was **0x73756a2d**, identical to the
failed dump. The third and launch-test clients started with **0x6f636e75**, whose
low three bits also encode invalid status 5. All took over without an assertion
and exited normally. This is repeatable evidence for the captured cause, not a
claim that every possible host-departure failure has been eliminated.

Autosave produced both A/B saves and both client copies. LoadCopy used a client
save from the Oct 22 baseline, before this fix; its SHA-256 remained
`7de50a888a0a30f70dd795b7b61583401f3a4fc7961f525781a9b32917eb960b`.
Both peers entered the loaded match. The existing source guard rejected the
client's unsupported save copy and rejected its unsafe autosave after takeover.

HostLeavesLaunch verified more than lobby survival: the new host entered a new
match, wrote `After takeover.RSG` (232,883 bytes), and exited normally. Its module
also completed an autosave before exit (232,800 bytes). That extra autosave is
retained evidence, not an additional counted check.

Final verification confirmed no remaining game processes, removal of the three
temporary rig files, and unchanged hashes for all 21 fixture files. See
`suite-final.json` and `final-verification.json` in the evidence directory.

The root cause, correction and results were shared with Ensrick in
[PR #7](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/7#issuecomment-6370715055).

## Remaining scope

Whole-current-patch network recovery, real two-machine latency/loss and direct
rejoining of a running match remain separate gates. Language and keybinding
differences are not used as admission restrictions. No packages or update feeds
have been published by this repair.

## Reproduce offline

Run `game/engine-crash-fixes/test_task_status.ps1` with `-Python <python.exe>`,
`-LegacyWork <verified analysis folder>` and `-OutputDirectory <test output>`.
The analysis folder must contain the hash-pinned mapped image and existing
Capstone/Unicorn dependencies. The script builds the C# transaction fixture,
exports the actual patch bytes and executes the original game instructions in
Unicorn. It does not attach to or start a game.
