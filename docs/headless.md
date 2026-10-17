# Headless commands

`PawsPatchLauncher.exe --headless <command>` runs the launcher without showing its window. The window is
created off-screen and hidden, and each command uses the same methods and controls as the window's buttons
and switches: the same availability rules, installer transactions, signed downloads and messages. Results and
errors are printed to the calling console or to redirected output.

```powershell
# PowerShell returns immediately from a windowed program unless its output is piped:
& .\PawsPatchLauncher.exe --headless status | Out-Host
& .\PawsPatchLauncher.exe --headless mods add "C:\Mods\Example.rwd" --enable --game vanilla | Out-Host
$LASTEXITCODE
```

`--test-profile=<folder>` works as it does for the window: settings, logs and downloads stay in that folder.

## Commands

| Command | What it does |
|---|---|
| `status` | Game folder, game version and support (all patch features or file changes only), selected game, channel, components, game language, local mods, applied modules, pending changes, the window's status line, and whether Launch is available. |
| `select-game vanilla\|immortals` | Selects the game, like the game choice on the Components page. |
| `set key=value ...` | Changes the selection like the Components page. Keys: `channel=release\|beta`, `paw`, `colors`, `ignore-desync`, `hostility`, `roaming`, `ai`, `lair` (`on\|off`), `spawn=standard\|x2\|x4`, `text=<language>`, `voice=<language>`. A key changes only when the page offers that component for the selected game, channel and game version, and its switch is enabled; otherwise the command fails with the page's reason. All pairs are validated before any change. |
| `mods [--game <game>]` | Lists the local RWD mods of the selected game (or `--game`), in load order; enabled mods lower in the list take priority. |
| `mods add <file.rwd> [--enable] [--game <game>]` | Imports an RWD with the window's import rules. A file with the same name as a listed mod replaces it in place, keeping its position and switch. New mods start off unless `--enable` is given. |
| `mods remove\|enable\|disable <name\|number> [--game <game>]` | Changes one listed mod; `number` is its position. |
| `mods move <name\|number> <position> [--game <game>]` | Changes the load order. |
| `apply` | Runs the window's primary action: Install or Update when the selection is not stored yet, then Apply settings. Waits for downloads and the installer transaction. |
| `launch` | Starts the game through Launch, with the same checks. The command returns once the game has started. |
| `help` | Lists the commands. |

Mod changes use the installer transaction of the window's "Apply mods": the applied game and its packages
stay as they are. Changes for the applied game are mounted immediately. A list for another game is saved
and mounted when that game is applied. Mods need an applied game first.

## Behaviour

- Exit codes: `0` done, `1` failed, `2` usage error, `3` a launcher window is open or the game is running.
- One launcher at a time: a command refuses to run while the launcher window is open (the window's
  single-instance rule). Changes and `launch` are refused while Kohan II is running; `status` and `mods` (list) work.
- Never interactive: popups are not shown, and anything that needs a confirmation fails instead of being
  confirmed.
- No launcher self-update, no account sign-in and no community chat in this mode. Game packages still come from
  the signed feed.
- A headless `launch` does not observe the game afterwards, so it leaves no crash-recovery record.
- Output text follows the launcher language (English or Russian). Field names (`status:`, `mods (vanilla):` ...)
  stay English for scripts.

## Checks

`PreviewRenderer.exe <output.png> --smoke-test --headless-checks --language=en|ru` runs every command, the
refusals and the stub launch against a signed local feed and a text-only fixture game, without network or a
visible window.
