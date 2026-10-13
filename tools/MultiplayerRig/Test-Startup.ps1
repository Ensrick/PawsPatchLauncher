# Starts one copy of Kohan II as the installed files are, waits for the main menu, quits, and reports what the game
# logged at start-up: every "ERROR" line, failed assertions and warnings. Exit code 0 when the main menu was reached
# with no error. A developer tool for checking an applied package without playing; it starts the real game (own
# Documents folder via the stand-in shfolder.dll, window parked off screen), so never run it while someone is playing.
param(
    [Parameter(Mandatory = $true)][string] $RigBuild,
    [string] $Game = 'C:\Program Files (x86)\Steam\steamapps\common\Kohan II',
    [int] $TimeoutSeconds = 120
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RigLauncher.ps1')
if (Get-Process k2 -ErrorAction SilentlyContinue) { throw 'Kohan II is already running; close it first.' }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam is not running.' }
$shfolder = Join-Path $Game 'shfolder.dll'
$scripts = Join-Path $Game 'PawsRig'
$script = Join-Path $scripts 'startup.txt'
if (Test-Path $shfolder) { throw 'the game folder already has a shfolder.dll' }
$logs = Join-Path $Game 'Logs'
$backup = Join-Path $env:LOCALAPPDATA 'PawsMultiplayerRigLogBackup'
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($f in Get-ChildItem $logs -File) { $to = Join-Path $backup $f.Name; if (-not (Test-Path $to) -or (Get-Item $to).Length -ne $f.Length) { Copy-Item $f.FullName $to } }
# Own Documents: the player's game data (hand-installed mods included), none of their own files, test settings.
$docs = Join-Path $env:LOCALAPPDATA 'PawsMultiplayerRig\Startup\Documents'
$data = Join-Path $docs 'Kohan2\data'
robocopy (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Kohan2\data') $data /E /XD Save saves CampaignSave DynamicCampaignSave Films Maps RandomMap Screenshots synclogs User /NFL /NDL /NJH /NJS /NP | Out-Null
New-Item -ItemType Directory -Force (Join-Path $data 'User'), $scripts | Out-Null
[IO.File]::WriteAllText((Join-Path $data 'User\UVars.tgi'), "[Vars]`r`n{`r`n`tflag`tResolutionWindowed = true`r`n`tint`tResolutionX = 800`r`n`tint`tResolutionY = 600`r`n`tflag`tMouseClipCursor = false`r`n`tflag`tRunWhenInactive = true`r`n`tfloat`tAudioMainVolume = 0`r`n`tflag`tFirstRun = false`r`n}`r`n", [Text.Encoding]::ASCII)
# The command line's script runs after start-up commands; the main menu is up once the game connects it.
"# start-up test (tools/MultiplayerRig/Test-Startup.ps1)`r`nWait 40`r`nEcho startup test: waited`r`nReallyQuit`r`n" | Set-Content $script -Encoding ascii -NoNewline
$known = @(Get-ChildItem $logs -Filter 'log-*.log' | ForEach-Object Name)
$id = $null
try {
    Copy-Item (Join-Path $RigBuild 'shfolder.dll') $shfolder
    $id = Start-RigGame $Game '+Exec PawsRig/startup.txt' "K2_RIG_DOCUMENTS=$docs`0"
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and -not $p.WaitForExit($TimeoutSeconds * 1000)) {
        $c = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
        if ($c -and $c.CommandLine -match 'PawsRig/startup\.txt') { Stop-Process -Id $id -Force; "stopped the test copy after $TimeoutSeconds s" }
    }
} finally {
    Start-Sleep 2
    if (-not (Get-Process k2 -ErrorAction SilentlyContinue)) {
        foreach ($f in $shfolder, $script) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f } }
        if ((Test-Path $scripts) -and -not (Get-ChildItem $scripts)) { Remove-Item -LiteralPath $scripts }
    }
}
$log = Get-ChildItem $logs -Filter 'log-*.log' | Where-Object { $known -notcontains $_.Name } | Sort-Object LastWriteTime | Select-Object -Last 1
if (-not $log) { 'no game log was written'; exit 1 }
$text = Get-Content $log.FullName
$menu = [bool]($text | Select-String -SimpleMatch 'connect MainMenuInterface' -Quiet)
$errors = @($text | Select-String -Pattern '^ERROR|ERROR:|ASSERT: |unhandled exception' | ForEach-Object { $_.Line.Trim() })
$warnings = @($text | Select-String -Pattern '^WARNING|WARNING:' | ForEach-Object { $_.Line.Trim() })
"game log $($log.Name): main menu $(if ($menu) { 'reached' } else { 'NOT reached' }), $($errors.Count) error line(s), $($warnings.Count) warning line(s)"
$errors | Select-Object -First 40 | ForEach-Object { "  $_" }
if ($warnings.Count) { '  warnings:'; $warnings | Select-Object -First 20 | ForEach-Object { "    $_" } }
exit $(if ($menu -and $errors.Count -eq 0) { 0 } else { 1 })
