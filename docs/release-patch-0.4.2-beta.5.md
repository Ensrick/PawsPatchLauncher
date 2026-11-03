# Paw’s Patch 0.4.2-beta.5 — Arcane Wars

- Fixed an identified cause of hangs when the host leaves: the initial network task state is now set before entering the lobby.
- Network error checks and save protections remain enabled. Direct joining of an already running match is not part of this update.

Validation: six isolated host/client runs passed 63 checks; 417 installer/rollback checks and 94 native-instruction cases passed. Network runs used stock Vanilla plus multiplayer saves and the repair on one PC. Full-patch two-machine recovery and direct hot-join remain separate validation work.
