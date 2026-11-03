# Paw’s Patch 0.4.1-beta.9 — Vanilla / Immortals

Fixed a possible recovery initialization deadlock in Vanilla and Immortals. File preparation now runs before game threads are paused, and diagnostic logging runs after they resume. Compatibility and save checks remain enabled. The shared lobby installer was also verified with Arcane Wars.

The fix removes a reproduced heap-lock dependency during the paused installation step. It does not bypass assertions or add direct joining of a running match. Arcane Wars continues to use its existing multiplayer behavior; Pure recovery remains specific to Vanilla and Immortals.
