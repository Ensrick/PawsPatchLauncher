# Reconnect research on 1.3.68

Observations for section 3 of [RWD, plugin API and reconnect](RWD_AND_RECONNECT.md). This is research input, not
a feature: nothing here is enabled in a release or feed.

- **Build:** everything was observed on Kohan II **1.3.68**, the public Steam branch (build 24805354). Nothing is
  re-mapped to 1.3.72 (the Steam `beta` branch, build 25068126) yet.
- **Addresses:** RVAs in the 1.3.68 `k2.exe`. They will differ in 1.3.72; the anchors table at the end gives
  strings and behavior to find each place again.
- **Method:** static reading of the executable, plus game logs from three kinds of runs. Some runs were two game
  copies on one PC (LAN, loopback). One was two real PCs over Steam. Others ran an in-process demonstrator
  (below).
- **Uncertainty:** "[inferred]" marks conclusions that the code suggests but no run has shown.

## 1. What the engine does when a peer stops answering

1. **Timeout.** `TGC_AdminConn::Update` (RVA 14F9A5) times a connection out after 2.0 s without packets, or 3.0 s
   for the connection to the server (log `NET: Timeout()`). When the server connection times out, the client calls
   `SetServer(self)` (`SetServer() Becoming server`). So after a host loss **every** survivor makes itself server.
2. **Backup mode.** The server recomputes a reason bitmask per player (`WantsToEnterBackupMode`, RVA 148CAD):
   - 2 = connection not up;
   - 4 = decree backlog over the bandwidth thresholds;
   - 1 and 8 = not resolved.

   Any reason makes it call `SetBackupMode(1)` (RVA 14F3B6, log `AdminNode::SetBackupMode(1)`, followed by one
   `Client(<name>) WantsToEnterBackupMode(<mask>)` line per player). That stores the flag at admin `+2F4` and a
   timestamp at `+2F8`. Backup mode is left silently, with no log line.
   - Admin `+2ED` is a different flag, "disconnecting" (`SetDisconnecting`, RVA 14F64B, log
     `NET: SetDisconnecting(...)`). Code that checks `+2ED` for backup mode is wrong; this mistake was made in the
     demonstrator below.
3. **Freeze.** While the host is in backup mode, its main update (RVA 14C758) runs no simulation tick. A backup
   server that is not the host produces no decrees. This is the frozen game with the connection dialog.
4. **Resolution.** RVA 14A6DE runs on the server every admin update. A player is removed (`PlayerLeave` via
   `LeaveDecree`, RVA 14A8C0) only in one of these cases:
   - **automatically**, when every lagging player has the "peer announced leaving" flag (connection `+B6`). Three
     things set that flag: a socket receive error (`Error on receive: %d`, `NET: Processing error on recieve`), a
     clean host shutdown (`ServerDisconnect` packet), or a client's leave request (`ClientDisconnectRequest`
     packet, RVA 15171A);
   - **by vote**, when the votes reach floor(n/2)+1 of the connected players, the server included, **and** every
     lagging player's grace credit has run out. The credit starts at its 15 s cap for a healthy player and is
     reset to 15 s when a peer becomes server. It drops by 1 s per second while the player lags and recovers at
     0.1 s per second while it does not, so a vote takes effect about 15 s after the drop at the earliest;
   - when the session counter at `+F0` is 1, the server's own vote is enough [inferred: one human member].

   With 3 or more players, a non-host server first needs every non-lagging peer's decree history to be current
   (`NET: Reverse history not up to date`).
5. **Takeover.** If the server that resolves this is not the host, `LeaveDecree` first makes it host
   (`SetHost(<name>)` in the log, text key `networking_new_host` on screen). The other survivors follow it.

**Consequences for Steam games [inferred]:**

- The Steam transport (`TGC_SteamSock`, ISteamNetworkingMessages) has no receive-error path, so a peer that crashes
  or loses its connection over Steam never sets `+B6`. Someone must vote (the "kick" toggle in the connection
  dialog, RVA BC51C). If nobody votes, the game stays frozen. That is a plausible explanation for freezes that
  "never recovered".
- On one PC with two copies, loopback produces `Error on receive: 10054` (connection reset) and the automatic
  path runs. Local two-copy tests therefore resolve drops on their own, which Steam games do not. Test on real
  separate PCs before drawing conclusions.
- The game has a console command `CSDKickLaggingPlayers`. Its effect was not verified.

## 2. A deliberate quit versus a drop

A player who leaves through the menu sends `ClientDisconnectRequest` (packet type 10). The host handles it at
RVA 15171A and then removes the player with the "quit" reason. A drop sends nothing. The demonstrator counted a
`PlayerLeave` within 3 s of that request as a deliberate quit, and anything else as a drop. That separated the two
in offline tests; no real-peer run has tested it.

## 3. Back to staging with everyone still connected

- The host's in-game "End Mission"/"Abort Mission" (RVA C3E7E) sends three orders: `SetNewState(1)` with the
  staging descriptor, `SetMapType(4)` ("use existing", keeps the world) and `Launch`. Every PC goes to staging
  (state 1) and nobody is disconnected.
- The menu offers that button only when the mission is over or only one human side remains. The demonstrator
  called the same function directly.
- In staging a returning player can join: `SerializeConnect` (RVA 1506DD) refuses joins only in game (state 2) and
  film (state 4).
- There is no path that adds a **human** to a running match. `MidgameCreateImplicitAIs` (RVA 168019) only creates
  AI players for kingdoms without a player.

## 4. Loading the resume save from staging

- The host selects a saved game with `KKC_HostSetLoadSavedGameOrder` (constructor RVA 12181C, issue RVA 12246E).
  The host-side handler (RVA 1561A2) looks up the file and sends the orders that make it the session's pending
  saved game. Clients then report their map status, and the stock staging transfer sends the save to a client
  that lacks it (210 KB in 13 s in the run in section 5).
- Sent at the moment staging is reached, the order was refused (log `Order %s rejected due to versioning`). Sent
  about 1.5 s later and confirmed, with up to 5 resends, it worked.
- Reusing a save name made the returning client ask "A different version of this map exists - overwrite it?", and
  Cancel exits staging. The demonstrator therefore wrote a unique name per drop
  (`MP Resume <date> <time>.RSG`).

## 5. Demonstrator results (1.3.68, not proposed as code here)

An in-process plugin did the following in a running multiplayer match:

1. On a drop, it saved and paused.
2. Once the save file stopped growing, it took everyone to staging with "End Mission".
3. It selected the save as in section 4.
4. It cast the local vote after 20 s with a connection down.

The plugin is in an archived repository: <https://github.com/Ensrick/kohan2-mod-manager> (`native/k2mp/dropguard.cpp`).

| Run | Result |
| --- | --- |
| Two copies, one PC, client drop then restart | Worked once end to end: save, staging, save selected, restarted client joined, 210 KB transferred in 13 s, launch, both back in the match |
| Same, earlier version | Failed at the overwrite prompt (section 4), which led to unique save names |
| Two copies, client drop and host drop | Saved and paused in both cases |
| Real PCs over Steam | Not run with the demonstrator active (see section 6) |

Known defect: the demonstrator tested `+2ED` instead of `+2F4` for backup mode. So its vote probably never fired,
and its host-takeover handling is unverified.

## 6. Host crash on 2026-10-12 (two real PCs over Steam, 1.3.68)

Host log, player names removed:

```text
[time 241.1] NET: Timeout()
AdminNode::SetBackupMode(1)
  Client(<host>) WantsToEnterBackupMode(0)
  Client(<client>) WantsToEnterBackupMode(2)
...                                          the client restarts its game and connects again
NET: Peer changed session id (...) - resetting conn sequencing
NET: SerializeConnect(I)  name = <client>, ...
NET: SetDisconnecting(1, 10)
NET: SerializeClientDisconnectDecree(O) Reason = 10      (repeated)
AdminClient::Reset()
AdminNode::SetBackupMode(1)
  Client(<host>) WantsToEnterBackupMode(0)
  Client(NO HUMAN PLAYER!?) WantsToEnterBackupMode(2)
thread MAIN caught unhandled exception - Access Violation (C0000005) trapped at <base>+14C2A9
```

- **What happened:** the client timed out and the host entered backup mode. The client then restarted and tried
  to join the running match. The host marked that connection as disconnecting (reason 10) and reset it, which
  left an administration entry with no player.
- **The faulting function** (RVA 14C286) counts active clients: for each entry in admin `+2CC` it reads the player
  at entry `+0C`, then `[player+28]`. With a null player that read faults.
- **Same bug as the network guard:** this is the null read that the network guard in
  `game/engine-crash-fixes` handles at 1.3.72 RVA 14C04A. The instruction order differs between the builds (the
  guard's expected bytes do not occur in 1.3.68), but the loop, offsets and null read match. On 1.3.72 with that
  guard, this sequence should no longer crash the host [inferred, untested].
- **Practical rule from this one observation:** on 1.3.68, a dropped player should not try to rejoin a running
  match.

## 7. Suggested next steps on 1.3.72

1. Re-map the anchors below and repeat sections 1 and 6 on real PCs over Steam: a client drop, a client crash and
   a host crash, each with no vote and with a vote. Record both peers' logs.
2. Decide whether a vote should be automatic after a fixed time (a launcher option, off by default), given that
   Steam drops never resolve without one.
3. Prototype the save, staging and reload flow from sections 3 and 4 as an opt-in demonstrator, following the
   steps in [RWD, plugin API and reconnect](RWD_AND_RECONNECT.md). Host loss needs separate design (section 1,
   step 5).

## Anchors for re-mapping

| 1.3.68 RVA | What | Find it by |
| --- | --- | --- |
| 14F9A5 | `TGC_AdminConn::Update`, timeouts | 2.0 and 3.0 s constants; `NET: Timeout()` caller |
| 14F58C | `SetServer` | `SetServer() Becoming server` |
| 14F3B6 | `SetBackupMode`, admin `+2F4`/`+2F8` | `AdminNode::SetBackupMode(%d)` |
| 148CAD | `WantsToEnterBackupMode` reasons | `WantsToEnterBackupMode(%d)` |
| 14F64B | `SetDisconnecting`, admin `+2ED` | `NET: SetDisconnecting(` |
| 14A6DE | resolution: auto, vote, single member | calls of 14A8C0 |
| 14A8C0 | `LeaveDecree` | `SetHost(%s)` caller |
| 15171A | `ClientDisconnectRequest` receive | packet type 10 |
| 1506DD | `SerializeConnect` receive | `NET: SerializeConnect(I)` |
| C3E7E | End Mission to staging | `SetNewState` + `SetMapType(4)` + `Launch` sequence |
| 12181C / 12246E | `KKC_HostSetLoadSavedGameOrder` ctor / issue | its RTTI name |
| 14C286 | active-client count (crash in section 6) | `[player+28]` loop over admin `+2CC` |
| BC51C | connection dialog kick toggle (casts the local vote) | `KickToggle` UI command; calls the vote at 14A5D0 |
