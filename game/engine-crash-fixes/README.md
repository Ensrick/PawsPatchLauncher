# Shared crash guards, revision 2

`TaskStatusPatch.cs` is a separate producer-side repair for the host-departure
assertion: initialize the native local task status before it can be sent.
It keeps the shared enum reader and its assertions intact. See the
[root-cause and verification report](../../docs/verification-task-status-2026-10-23.md)
and `test_task_status.ps1` for its native-instruction and installer tests.

Opt-in `-EngineCrashFixes` for the full Arcane Wars helper build. This does not
publish a release or alter game assets. For a helper launched outside the game
directory, also use the existing `-LairWoundedTest` local-data mode: it resolves
the presentation, language and palette from the verified installation.

## Evidence and scope

- Friend log 24 on 2026-09-20: native animation update at RVA `31EAB5` calls
  a data address, RVA `57CA1C`, through the target's type-query slot. This matches
  the earlier dangling animation target signature. The current minidump does
  not contain enough heap data to name its animation asset.
- The earlier full diagnostic identified one destroyed target in a sequence
  of 27 controllers (zero references and a destructed base-class vtable).
- Local logs 354 and 355: RVA `14C04A` reads `[eax+28]` when the player pointer
  at administration-entry `+0C` is null. Disconnect/reset precedes the first
  load failure. A later load succeeded; these dumps do not prove save damage.

## Animation guard

At `31EAA6`, preserve the original target load, all integer registers and flags.
Check the target reference count, native read-only vtable range and executable
type-query range before the original virtual call. A bad target skips only this
controller's current update, using the stock next-controller branch at `31EB1A`.
Valid and null targets retain the original update semantics. No reference counts,
target pointers, ownership, assets or simulation objects are changed.

An OS-registered vectored handler handles read access violations only inside the
new probe. It returns to the probe's own failure exit, never into a faulting game
routine. Writes, execute violations and faults at game addresses propagate.
No dynamic SEH-chain entry or Windows mitigation changes are used. The handler
remains until process exit, like the patch code. Failed registration preserves
stock behavior and records a failure once; there is no per-controller retry.

This addresses the observed invalid animation-target class, not every possible
animation crash or the underlying lifetime bug. The separate stop guard below
also rejects readable objects recycled into a different native class.
The guard does not call `VirtualQuery` or allocate per frame. The one-time
handler registration uses the verified corresponding Windows DLL entry point.

## Network guard

At `14C047`, omit an entry with a null administration/player pointer from the
existing active-client count. Keep the original result for all valid entries
and continue through the list. Do not change membership, packets, loading state,
kingdom ownership, saves, simulation or the optional desync bypass.

## Installation and diagnostics

All hooks have exact instruction/context checks before mutation. The code page
is RX and the counter page RW. Partial writes roll back; uncertain rollback retains
the allocation. Installation applies only to the newly launched verified process.

`ENGINE_CRASH_FIXES` and changed `ENGINE_CRASH_COUNTERS` are appended to the
existing helper status log, already covered by launcher diagnostics. Counters
distinguish dead targets, invalid vtables, invalid type-function addresses,
unreadable targets and missing network clients. These are skipped operations,
not a proven count of crashes prevented. No automatic upload is added.

## Observer team commands

The observer F macro reaches the native ally list with no local kingdom. The
September 27 dump reproduces its null read at RVA `B71A9`. Guard all four command
entry points before changing UI state, using the native local-kingdom getter.
Observers return without issuing a command; players execute the original
instructions with registers and flags preserved. The `TeamCommand` SEH descriptor
is relocated in both validation and rollback. No simulation state is changed.
The diagnostic counter is `observerTeamCommandsIgnored`.

## Sequence deactivation (October 16 local candidate)

The friend's October 15 `log-70.dmp` calls RVA `31CEAE` with the
`NiMaterialProperty` vtable at RVA `53C3C0`. Its purported Stop slot (`+40`)
is actually the end of the adjacent `m_amb` string, value `0x62`. This is an
invalid controller identity, not a valid controller with a broken animation.

Replace the complete virtual-call sequence at `31CEAA` with a bounded probe.
The pinned executable's MSVC RTTI supplies all 36 `NiTimeController` family
vtables at build time. A positive reference count, exact family vtable and an
executable Stop slot are required. No virtual type getter is called on the
suspect object. Read faults are handled only inside this new probe, using the
existing scoped Windows VEH. All registers/flags are restored before either
the original Stop(0) call or the stock next-controller branch (`31CEB1`).

An invalid entry skips only that Stop call. The array, reference counts and
ownership remain untouched. `ENGINE_ANIMATION_STOP` reports the reason,
controller, sequence and last readable vtable. This guards the demonstrated
call, but does not establish which code freed/recycled the controller or
guarantee that every subsequent use of that object is safe. It does not catch
errors thrown by a valid Stop function or graphics driver.

The recorder now also captures first-chance execute access violations, such
as a call to `0x62`, outside driver modules. It still propagates the exception
unchanged, takes at most one full dump per run and detaches. Safe read probes
do not trigger this new capture condition. Resource creation/release hooks
are not added: these dumps do not yet justify changing the graphics lifetime
path. NVIDIA crash causality remains unresolved.

## Validation

- `test_native.py`: 567 instruction-level cases across three ASLR bases. Uses
  stock loop instructions with controlled objects and mocked type/update callees;
  compares valid behavior, one bad controller among 27, and client counts 0–7.
  Reproduces the exact `0x62` crash, tests all 36 controller types and continues
  past a recycled material, null and dead entries without changing ownership.
- `TransactionTests.cs`: 1081 checks including the real Windows API resolver,
  relocation, identity rejection, partial writes, rollback and uncertain rollback.
- `ProbeWindowsTests.cs`: 95 checks in a disposable x86 Windows process. Executes
  the generated probe and real exception dispatcher with inaccessible target and
  vtable pages, page-boundary reads, unaffected valid probes, unrelated-exception
  rejection and simulated registration failure. Does not start the game.
- `graphics-diagnostics/test.ps1`: 9 disposable process scenarios, including
  a handled invalid call at `0x62`, preserved original crash handling, safe
  recorder exit and process-identity rejection.
- The full helper build also runs the existing feature regression suites.

The October 16 local candidates target Arcane Wars `0.4.2-beta.1` data and
Vanilla/Immortals `0.4.1-beta.3` data. Their actual EXE hash participates in lobby
compatibility: all peers must use the same test helper variant and compatible
settings. Live game validation remains a separate manual step.
