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
not the contents, so a writer must only give equal stamps to equal contents.

## What the writer guarantees

- **Deterministic output.** Files are stored in lower-cased ordinal name order with load indices 0..n-1, the
  filler fields are zero, and the stamp (unless `--stamp` is given) is the CRC-32 of a SHA-256 over every file's
  lower-cased name, a NUL and its bytes, in that order. The same files give a byte-identical archive on any PC and in
  any input order; one changed byte changes the stamp.
- **Safe names.** Reading and writing reject empty or `.`/`..` segments, rooted paths, drive letters, control
  characters, the characters `< > " | ? *`, segments ending in a space or dot, and names that differ only in case.
  `extract` also checks each joined path stays under the target folder and keeps existing files unless
  `--overwrite`.
- **Bounded reading.** Every section and entry is checked against the file size before use; truncated or damaged
  archives fail with a message instead of reading out of bounds. At most 1,000,000 entries and 1,024-character names.
- **Atomic writes.** The archive is written to `<archive>.tmp` and moved over the target when complete.

## Evidence

On a Windows PC with the Steam release of game version 1.3.68 (no game process involved, archives only read). The
1.3.72 Steam `beta` branch (build 25068126) ships the same `Data.rwd` and `Music.rwd` byte for byte (equal SHA-256),
so these results hold for 1.3.72 too:

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

## Not covered yet

From the list in `docs/RWD_AND_RECONNECT.md`, still to establish before launcher integration:

- The engine's load precedence between two archives that contain the same file, and against a loose override. This
  needs an in-game check; archive names alone do not decide it.
- How installation, disabling, reordering and removal keep ownership, hashes and rollback in the launcher.
- Comparing effective content and load order between multiplayer peers.
- The meaning of the per-entry u32 written as 0 (possibly flags or compression). It is 0 for all 10,557 entries of
  the retail archives; the reader rejects an archive with any other value rather than guess.
