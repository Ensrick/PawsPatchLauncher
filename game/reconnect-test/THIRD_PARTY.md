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

Kohan II and Immortals files are read from the user's existing installation
solely to prepare their local PC/VM test. They are not included in this source
repository or published as part of this experiment.
