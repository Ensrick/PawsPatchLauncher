# Recovery and autosave coordination (local, 2026-10-13)

Our side of the `PawSaveBusy` proposal from
[Ensrick's PR #9 reply](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/9#issuecomment-6144485943).
The [contract reply](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/9#issuecomment-6144614912)
assigns the adapter change to us and the narrowed autosave/client-copy module to Ensrick.
This is a local follow-up to the [source-header guard](ensrick-followup-review-20261013.md), not a release.
No game was launched for this step; live acceptance was deferred by the user.

## Contract

Each cooperating module exports an aligned writable data symbol:

```c
__declspec(dllexport) volatile LONG PawSaveBusy = 0;
```

The owner alone writes its flag with `InterlockedExchange`. Automatic save producers must run on the game's
main thread, check the native `session+0x134` slot and all other participants immediately before queueing, then
publish their own busy flag before issuing `GameSave`. Every non-null native slot is occupied, including a
completed task that the engine has not detached. A foreign flag covers the interval before that task exists.

Our flag remains nonzero through saving and the entire save/lobby transition. It clears when recovery finishes,
cannot queue its save, fails, times out, or loses its session/host. Waiting for a foreign save does not set our
flag, so both modules do not wait on each other. Each peer must follow the contract; the old PR #9 binary at
`f9264f2` does not. During this step Ensrick supplied the narrowed module at
`5652cd97438769b6d058ab9f62b4a61f11b85e01`; the binary-level checks below use that exact source.

The flag is a scheduling signal, not a cross-thread lock or cancellation API. It does not intercept a manual
save command queued by the unmodified engine before a task appears, and resetting/abandoning recovery does not
cancel an already dispatched engine command. The native slot must still be checked after any module clears its
flag. In-game timing, especially timeout/late-command behavior, remains an acceptance requirement.

## Retaining a drop while saving is busy

A drop is bound to its admin/session/world and retained for up to 120 seconds. Multiple drops coalesce without
extending that deadline. Retry happens at most four times per second, only while a drop is pending. The normal
idle tick does not scan loaded modules. A changed match, host, lobby or countdown discards the pending drop.
Expired/stale work is discarded before consuming new events, so a fresh drop in that tick is not lost behind it.
There is no new pause, save name, save command or lobby transition while the operation is deferred.

Module discovery uses a Windows Toolhelp snapshot. Each module is temporarily referenced with
[`GetModuleHandleExA`](https://learn.microsoft.com/en-us/windows/win32/api/libloaderapi/nf-libloaderapi-getmodulehandleexa)
while its data export is read, then released. No unpinned pointer survives the scan. Discovery failure, a concurrent
unload or an invalid export defers automatic saving. A function export is never invoked as if it were data.
The TinyCC bundle lacks `tlhelp32.h`; the small local compatibility declaration follows the documented
[`MODULEENTRY32` layout](https://learn.microsoft.com/en-us/windows/win32/api/tlhelp32/ns-tlhelp32-moduleentry32),
with an x86 size assertion. Neither the compiler installation nor system headers were changed.

## Validation

- 133 native recovery assertions and 24 integrated adapter assertions; all 34 exact guards remain enabled as
  before. The source-resource guard from PR #11 is retained.
- 103 new coordination assertions exercise the real adapter tick with engine-memory fixtures and recorded
  commands. Foreign busy/idle/malformed exports use actual loaded DLLs; both built recovery DLLs expose the
  expected data symbol. Native pending/completed-but-attached tasks, queued commands without a task, command
  failure, task failure, timeout, stale events, successful completion and module unload are covered.
- A same-tick new-match/drop regression failed before correcting stale-pending disposal and passed afterward.
- Four independently compiled negative controls fail: ignoring the foreign flag, ignoring the native task,
  removing our busy publication, or losing a deferred drop. These are shadow sources, not edits to the adapter.
- All four Pure helper variants compile and reproduce byte-for-byte in the deterministic rebuild.
- Existing offline composition: 804 guarded installs, 477 random-map checks, 24 lobby identity checks, and
  145 option checks for each AI profile.
- The additional `Block` check compares 80 relocated bytes at RVA `1CE7E9` before/after each of the existing
  12 offline Pure compositions (three image bases, AI/defenders on/off). All remain unchanged. The 18-byte prefix
  expected by PR #9 is `51 51 55 8B E9 8B 45 00 FF 50 10 8B CD 84 C0 74 0A E8`.
  This is offline composition evidence, not a packaged in-game run or verification that client copies load.

Local evidence/builds: workspace `outputs/save-coordination-20261013/`. Native test executable:
`native/SaveCoordinationTests.exe`; negative-control results: `mutations.json`; helper checks:
`helpers/composition-tests.txt`. The helper output is not installed and keeps the previous build version label;
it must not be substituted into a public feed under that identity.

### Updated PR #9: mutual coordination

The five files under `game/multiplayer-saves` at `5652cd9` were exported unchanged into the local evidence folder.
Its build passes 79 offline assertions, 105 including the pinned image (includes those 79), and 11 installer
configuration assertions. This does not merge or install the module.

Our 103-check suite also passes using its actual `paws_multiplayer_saves.dll` as the foreign participant.
In the other direction, an additional harness includes its unchanged source and existing engine-memory fixtures,
replaces only engine command dispatch through its provided test callback, and restores its real module discovery.
It passes 19 assertions against both of our actual DLLs: recovery busy defers autosave, clearing it permits one
command, the queued/native-task interval stays busy, native task detachment releases it, and a malformed export
defers. Evidence: `Pr9AdapterContract.exe`, `pr9_adapter_contract.c` and `pr9-native/`.

The subsequent installer integration and real Vanilla/Immortals acceptance are recorded in
[the 0.4.1-beta.2 validation](release-pure-0.4.1-beta.2-validation.md). That follow-up checks the packaged modules
together, including save-copy loadability and received-save host loss. Recovery remains save/lobby recovery;
direct rejoining into an ongoing simulation is still unimplemented. Localization/keybinding admission rules
are unchanged.
