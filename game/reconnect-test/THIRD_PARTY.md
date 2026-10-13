# Ensrick attribution

This local experiment adapts the recovery design, native function research,
plugin interface, transfer pacing and private-folder test approach from
[Ensrick/kohan2-mod-manager](https://github.com/Ensrick/kohan2-mod-manager),
tag `v0.1.0-beta.12`, commit `9092a0dd00d0cc74d7c9f4ef85292d8ab0add77b`.
Copyright (c) 2026 Ensrick. The MIT license is included in `LICENSE.Ensrick`.

The unmodified `avifil32.dll` loader is taken from the user's existing
installation of that release. Its hash is recorded in the test kit manifest.
The corresponding loader source is `native/loader` at the pinned upstream tag.

Paw's changes: verified 1.3.72 call sites and code guards, C adapter, per-player
intentional-exit tracking, retained native save-task completion, lifecycle
checks, private test launcher, separate Vanilla/Immortals profiles and tests.

Revision r2 also uses the corrected backup/disconnecting interpretation in
[Ensrick's reconnect notes](https://github.com/Ensrick/PawsPatchLauncher/blob/fb067ca213aa0741ec2e7add15b790d9e87e7547/docs/reconnect-research-1368.md).
The offsets were checked independently in 1.3.72. Paw's r2 adds original-host
tracking, optional bounded timeout voting, and the existing Paw null-player
guard at the verified active-client counter. It does not suppress assertions.

The local save-source preflight follows Ensrick's
[PR #11](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/11), commit
`5c1eabfe234d26e7ac94293b6525a58942331dc3`: skip recovery's save when the game's
source-resource walk would fail, including a replacement host that received the save over the network.
Paw's follow-up adds rejection tests for each new guard in the integrated composition
and keeps the diagnostic general enough to cover missing resources and settings as well as unloaded headers.

The shared `PawSaveBusy` data export follows Ensrick's
[PR #9 coordination proposal](https://github.com/VasiliyPaw/PawsPatchLauncher/pull/9#issuecomment-6144485943).
Paw's implementation adds guarded module discovery, retained drops bound to their match, a bounded retry,
coordination throughout lobby return, and real-DLL/tick tests. The adapter itself does not implement autosaves or copies.
Pure 0.4.1-beta.2 pairs it with Ensrick's separately guarded `game/multiplayer-saves` module from PR #9,
merged at `fb091d31f4322cd4499f961f091f4d801470b301`. Paw's integration adds a compile-time beta gate,
waits for the delayed message-pump import during startup, and includes its messages in the existing diagnostic log.
It also supplies the save-data directory explicitly and acknowledges a changed, closed autosave output when
the native task completed between message-pump polls. Real-file regression tests cover the writer-lock boundary.

Kohan II and Immortals files are read from the user's existing installation
solely to prepare their local PC/VM test. They are not included in this source
repository or published as part of this experiment.
