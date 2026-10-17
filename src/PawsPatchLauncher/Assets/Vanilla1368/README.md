Empty new-game lobby defaults of Kohan II 1.3.68, the Steam public build 24805354
(k2.exe SHA-256 CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A).

`SingleLaunchSettings.bin` (2324 bytes) and `MultiLaunchSettings.bin` (2606 bytes)
were serialized by that original executable on 2026-10-17, using an isolated empty
user depot and `+Quit`, the same way as `../Vanilla1372`. They contain no account,
personal settings or saved game. The chunks carry the build's version stamp
("1.3.68") and the map-header SKU "English K2 Dev"; they differ from the 1.3.72
defaults only by that stamp, the SKU label and one 8-byte value, so each build only
receives the defaults its own executable wrote. GameLobbyPreferences verifies their
hashes before replacing an incompatible launch-settings chunk on a 1.3.68 game.
