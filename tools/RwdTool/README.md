# RwdTool: Kohan II .rwd archives

A standalone reader and writer for Kohan II resource archives (`.rwd`), with `list`, `verify`, `extract` and `pack`
commands. It needs no game installation, has no dependencies beyond the .NET 8 base library, is original code under
the repository's MIT license. It began as the format demonstrator in
[RWD, plugin API and reconnect](../../docs/RWD_AND_RECONNECT.md). The CLI does not install anything into a game;
the launcher separately uses this library for the [custom RWD mod manager](../../docs/custom-rwd-mods-local.md),
published in 0.9.3.

The original reader/writer and format research were contributed by
[Ensrick in PR #1](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/1), then hardened in
[PR #8](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/8). PR #1 is closed as superseded with the author's agreement;
its commits and review remain the contribution record. Ensrick also reported in `741a648` that the 1.3.68 and
1.3.72 retail Data/Music archives have identical SHA-256 hashes; our current measurements below use 1.3.72.

```powershell
dotnet build tools/RwdTool/RwdTool.csproj -c Release
$tool = "tools/RwdTool/bin/Release/net8.0/RwdTool.exe"
& $tool list    <archive.rwd>                      # stamp, then load index, offset, size and name per file
& $tool verify  <archive.rwd>                      # checks the structure and reads every file
& $tool extract <archive.rwd> <folder> [--overwrite]
& $tool pack    <folder> <archive.rwd> [--stamp <hex>]
```

Exit code 0 on success, 1 for a rejected or unreadable archive or folder, 2 for wrong arguments.

## Format

Little-endian throughout.

| Part | Content |
| --- | --- |
| Start | `"TGCK"`, u32 version (2), u32 section count (3) |
| `Header` section | u32 2, u16 *n* + UTF-16 game id (*n* characters: `K2`), u32 0, **u32 stamp** (the section's last 4 bytes) |
| `Files` section | every file's bytes, back to back; load-index order verified for the sampled retail archives and produced by this writer |
| `Footer` section | u32 file count; per file: u16 *n* + UTF-16 name (*n* characters, `/` separators), u64 offset from the start of `Files`, u64 size, u32 0, u32 load index |
| Section table | the last 96 × count bytes: per section, its UTF-16 name padded to 64 bytes, u64 offset, u64 size, u32, u32, u64 size |

The **stamp** is what the game's multiplayer join check compares per archive: the game logs its checksum string as
`checksums = Data.rwd dbf9c0e8 data/ 0 Music.rwd 89317623 ...`, and `RwdTool list` reads exactly `dbf9c0e8` and
`89317623` from the headers of the retail 1.3.68 `Data.rwd` and `Music.rwd`. The check therefore sees the stamp,
not a full content hash. A 32-bit stamp can collide, and `--stamp` can override it.
It must not be treated as proof that two archives contain identical gameplay data.

In the sampled 1.3.72 installation, the indices form a permutation of 0..n-1 in both retail archives.
Reading entries in index order traverses `Files` contiguously: all 10,526 Data entries and all 31 Music entries.
Data has one empty file sharing its offset with the following entry, so offset alone does not uniquely order
every entry. This is observed format evidence, not a requirement inferred for all third-party archives.
The reader does not depend on footer entry order. CLI `verify` reports a non-permutation as a warning;
it does not currently check whether indices follow payload order.

## What the writer guarantees

- **Deterministic output.** Names are stored lower-cased in ordinal order with load indices 0..n-1 and zero
  filler. Input order and name casing do not change output. The default stamp uses the length-framed v2 rule
  below, fixing the old ambiguous concatenation of names and payloads. Full archive SHA-256 remains the stronger
  comparison; changing the payload cannot be guaranteed to change a 32-bit stamp.
- **Safe names.** Reading and writing reject empty or `.`/`..` segments, rooted paths, drive letters, control
  characters, the characters `< > " | ? *`, segments ending in a space or dot, and names that differ only in case.
  Windows device names (including `NUL.txt`, `CON`, `COM1`, and `LPT²`) and invalid UTF-16 are also rejected.
  On Windows, the folder explicitly selected for `pack` or `extract`, and the parent of an output archive,
  may be a junction or lie under a relocated directory. The tool opens that directory once, resolves its
  physical location, locks the physical ancestors, and confirms the directory identity before using it.
  Directory handles request list access and deny delete sharing; attribute-only handles do not prevent
  directory replacement. All subsequent tree operations use the pinned physical path. Links below the selected
  root, including dangling links, remain rejected. Archive reads use one validated file handle without rejecting
  linked parent directories; the archive itself must be a regular file. Explicit `WriteFiles` source paths may
  also have linked parents, while tree packing rechecks parents on each payload open.
  The caller must own the operation tree; in-place permission/reparse-metadata changes by a concurrent process
  are outside this concurrency contract. On other systems, selected directory roots retain strict link rejection
  and callers must exclusively control the tree while the operation runs; there is no guarantee against hostile
  concurrent symlink swaps.
  `extract` prechecks file/directory collisions and keeps existing files unless `--overwrite`. Overwrites replace
  directory entries atomically, so they do not modify an outside file through an existing hard link.
- **Bounded reading.** Every section and entry is checked against the file size before use; truncated or damaged
  archives fail with a message instead of reading out of bounds. Limits: 100,000 entries, 1,024-character names,
  64 MiB metadata and 16 GiB total expanded payload. Unknown versions, duplicate/overlapping sections and malformed
  headers are rejected. CLI pack/verify/extract stream payloads in 64 KiB chunks instead of loading all files into
  memory. The convenience `Read` API rejects entries larger than 64 MiB; use `CopyTo` instead.
- **Atomic writes.** Archives and individual extracted files use unique, exclusively created temporary siblings.
  Only the operation's own temporary file is cleaned up after failure. An existing `<archive>.tmp` is untouched.
  Extraction is atomic per file, not a transaction for the whole directory.

### Stamp v2 framing

SHA-256 input, in this exact order (integer fields are little-endian):

1. ASCII `PAW-RWD-STAMP-2` plus a zero byte, then u32 entry count.
2. For each canonical name in ordinal order: u32 UTF-8 name byte count, UTF-8 name bytes,
   u64 content byte count, then those content bytes.
3. Store CRC-32 (IEEE) of the 32-byte SHA-256 digest as the TGCK header stamp.

This intentionally changes the default stamps from the initial, unpublished PR tool. `--stamp` remains an
explicit research override; it must not be used to disguise differing multiplayer content.

## Evidence

### Current writer: read-only retail comparison, 2026-10-13

Measured against the local Steam 1.3.72 installation. The game was not started; original archives were only read.
Full hashes, method and the limits of these observations are recorded in
[RWD format evidence](../../docs/rwd-format-evidence-20261013.md).

- `verify` reads all 10,526 files (385,157,324 bytes) of the retail `Data.rwd` and all 31 files (86,957,244 bytes)
  of `Music.rwd`.
- `extract` of `Music.rwd` followed by `pack --stamp 89317623` gives an archive of the same size (86,960,244 bytes)
  with the same section offsets and sizes. The header, complete `Files` section and all 31 individual payloads
  are byte-identical. All 31 names change to lower case, footer entries change order, and the section-table
  filler becomes zero. Per-file offsets and load indices stay the same. The older case-preserving writer's
  measurement omitted the name-case difference; that description does not apply to the current writer.
- Retail section-table filler pairs are `0x002ff4a8, 0x0012fe60` (Data) and `0x002fb630, 0x0012fe60` (Music).
  Resemblance to leftover memory addresses is a hypothesis; their meaning is not established.

### In-game observations: separate resource-loading paths

- Ensrick's [PR #8 review](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/8#pullrequestreview-5501117737)
  reports 1.3.72 loading archives with zero filler. A lower-case stored script name was found by a mixed-case
  `+Exec` request, and a mixed-case stored name was found by a lower-case request. This tests the script loader;
  case behavior for textures and audio is **not established** by these probes.
- His `addmod` script probe found the last archive wins, including over `<game>/data`, while
  `Documents/Kohan2/data` overrides those sources. A [follow-up control and repeat run](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/8#issuecomment-6142421272)
  confirmed `<game>/data` alone is read. These are reported contributor results, not a fresh local game run.
- Our separate [launcher integration checks](../../docs/custom-rwd-mods-local.md#evidence) used menu-label
  resources and `adddepot`, including generated mounts, reversal of archive/loose-directory order and disabling.
  They establish the tested launcher path; `addmod` results alone would not establish that path's behavior.

Neither resource-loading probe establishes multiplayer state equivalence or universal case behavior for every
engine resource loader. The Music comparison above verifies bytes, not playback of the repacked archive.

### Automated checks

Automated checks: `RwdArchiveTests` in `tests/PawsPatchLauncher.Tests` (part of the normal test run) builds small
self-authored archives and checks the round trip, input-order independence, a golden stamp and archive hash for a
fixed five-file input (so a change to the output must be deliberate), stamp sensitivity, explicit stamps,
empty files, non-ASCII names, case-insensitive lookup, every rejected name class, a hostile `..` name, a non-zero
unknown entry field, and truncated, mislabelled and out-of-bounds archives. No game files are used.

Local hardening on 2026-10-13 adds the ambiguous-boundary collision fixture, nested Windows junction rejection,
foreign temporary-file preservation, failed-write rollback, device names, forged entry offsets, malformed
metadata, case-independent output, and a streaming round trip larger than 64 MiB with a bounded allocation check.
The v2 golden stamp and archive SHA-256 were independently calculated with Python `struct`, `hashlib` and `zlib`.
Read-only `verify` also passes for Data.rwd (10,526 entries) and Music.rwd (31 entries) in the local Steam 1.3.72
installation. Neither this nor format tests establish in-game load precedence.

The relocated-path follow-up tests linked roots and ancestors, chained links, missing destination folders,
byte-identical archives, input/output aliases, self-overwrite rejection and preserved outside sentinel files.
It also replaces an alias after opening a root, attempts to rename the pinned physical directory,
and swaps a nested input directory between enumeration and payload reading. Run the focused archive and
launcher-import checks with `dotnet run --project tests/PawsPatchLauncher.Tests -c Release -- --rwd`.

## Remaining limits

- Other resource loaders' name-case and precedence behavior, especially textures and audio, needs its own live
  evidence. Archive names alone do not decide load priority.
- Launcher installation, disabling, reordering, ownership and rollback are covered by the separate integration
  checks linked above. Those tests do not certify every third-party mod or relax the general installer's path rules.
- Comparing effective simulation content across differently packaged archives is not implemented by this tool.
  The launcher includes enabled archive bytes and order in its existing module report; this does not establish
  semantic equivalence or make different localization/keybind settings a new admission rule.
- The meaning of the per-entry u32 written as 0 (possibly flags or compression). It is 0 for all 10,557 entries of
  the retail archives; the reader rejects an archive with any other value rather than guess.
