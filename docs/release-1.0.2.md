# Paw’s Launcher 1.0.2

Fixed custom RWD mod import from FAT/exFAT drives when Windows rejects the file-attribute query (error 87). The fallback validates the same open file handle and retains link and file-type checks.

Thanks to Ensrick for [PR #18](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/18).

Validation: 162 focused RWD checks; full launcher regression suite; read-only verification of 10,526 entries in Data.rwd. Local volumes are NTFS; the error-87 branch is covered by injected query results. Ensrick separately reports a successful actual exFAT import and in-game test. FAT32 and network shares have not been independently verified.

Game packages are unchanged.
