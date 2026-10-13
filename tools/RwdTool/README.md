# RwdTool: Kohan II .rwd archives

A standalone reader and writer for Kohan II resource archives (`.rwd`), with `list`, `verify`, `extract` and `pack`
commands. It needs no game installation, has no dependencies beyond the .NET 8 base library, is original code under
the repository's MIT license, and is the first step proposed in
[RWD, plugin API and reconnect](../../docs/RWD_AND_RECONNECT.md): format evidence before any launcher integration.
It does not install anything into a game and does not change the launcher.

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
| `Files` section | every file's bytes, back to back, in load-index order |
| `Footer` section | u32 file count; per file: u16 *n* + UTF-16 name (*n* characters, `/` separators), u64 offset from the start of `Files`, u64 size, u32 0, u32 load index |
| Section table | the last 96 × count bytes: per section, its UTF-16 name padded to 64 bytes, u64 offset, u64 size, u32, u32, u64 size |

The **stamp** is what the game's multiplayer join check compares per archive: the game logs its checksum string as
`checksums = Data.rwd dbf9c0e8 data/ 0 Music.rwd 89317623 ...`, and `RwdTool list` reads exactly `dbf9c0e8` and
`89317623` from the headers of the retail 1.3.68 `Data.rwd` and `Music.rwd`. The check therefore sees the stamp,
not a full content hash. A 32-bit stamp can collide, and `--stamp` can override it.
It must not be treated as proof that two archives contain identical gameplay data.

## What the writer guarantees

- **Deterministic output.** Names are stored lower-cased in ordinal order with load indices 0..n-1 and zero
  filler. Input order and name casing do not change output. The default stamp uses the length-framed v2 rule
  below, fixing the old ambiguous concatenation of names and payloads. Full archive SHA-256 remains the stronger
  comparison; changing the payload cannot be guaranteed to change a 32-bit stamp.
- **Safe names.** Reading and writing reject empty or `.`/`..` segments, rooted paths, drive letters, control
  characters, the characters `< > " | ? *`, segments ending in a space or dot, and names that differ only in case.
  Windows device names (including `NUL.txt`, `CON`, `COM1`, and `LPT²`) and invalid UTF-16 are also rejected.
  `pack`, `extract`, and archive reads reject existing links/reparse points at every path component, including
  their roots. Windows operations retain ancestor directory handles without delete sharing to prevent those
  directories being replaced during use. On other systems, callers must exclusively control the tree while
  the operation runs; there is no guarantee against hostile concurrent symlink swaps.
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

On a Windows PC with the Steam release of game version 1.3.68 (no game process involved, archives only read):

- `verify` reads all 10,526 files (385,157,324 bytes) of the retail `Data.rwd` and all 31 files (86,957,244 bytes)
  of `Music.rwd`.
- `extract` of `Music.rwd` followed by `pack --stamp 89317623` gives an archive of the same size (86,960,244 bytes)
  with the same section offsets and sizes, and a header and `Files` section byte-identical to the original. It
  differs only in the footer's entry order (the retail footer is not in name order; the reader does not depend on
  that order) and in the first 8 bytes after each section-table record's size, which are zero here and
  `30 b6 2f 00 60 fe 12 00` in the retail file (they look like leftover memory in the original packer).
- Not yet shown: that 1.3.72 loads an archive from this writer, and with which precedence (see below).

Automated checks: `RwdArchiveTests` in `tests/PawsPatchLauncher.Tests` (part of the normal test run) builds small
self-authored archives and checks the round trip, input-order independence, a golden stamp and archive hash for a
fixed five-file input (so a change to the output must be deliberate), stamp sensitivity, explicit stamps,
empty files, non-ASCII names, case-insensitive lookup, every rejected name class, a hostile `..` name, a non-zero
unknown entry field, and truncated, mislabelled and out-of-bounds archives. No game files are used.

Local hardening on 2026-10-13 adds the ambiguous-boundary collision fixture, Windows junction rejection,
foreign temporary-file preservation, failed-write rollback, device names, forged entry offsets, malformed
metadata, case-independent output, and a streaming round trip larger than 64 MiB with a bounded allocation check.
The v2 golden stamp and archive SHA-256 were independently calculated with Python `struct`, `hashlib` and `zlib`.
Read-only `verify` also passes for Data.rwd (10,526 entries) and Music.rwd (31 entries) in the local Steam 1.3.72
installation. Neither this nor format tests establish in-game load precedence.

## Not covered yet

From the list in `docs/RWD_AND_RECONNECT.md`, still to establish before launcher integration:

- The engine's load precedence between two archives that contain the same file, and against a loose override. This
  needs an in-game check; archive names alone do not decide it.
- How installation, disabling, reordering and removal keep ownership, hashes and rollback in the launcher.
- Comparing effective content and load order between multiplayer peers.
- The meaning of the per-entry u32 written as 0 (possibly flags or compression). It is 0 for all 10,557 entries of
  the retail archives; the reader rejects an archive with any other value rather than guess.
