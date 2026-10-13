# Which copy of a data file Kohan II 1.3.72 uses when several depots hold it: two archives mounted with `addmod`, the
# game folder's data\, and the player's Documents\Kohan2\data. One copy of the game per case, with its own Documents
# folder (stand-in shfolder.dll), started with `+Exec pawsprobe/order.txt`; each candidate file is a script that echoes
# its own source and quits, so the game log names the winner. A developer tool for docs/RWD_AND_RECONNECT.md section 1;
# it starts the real game, so never run it while someone is playing. Needs RwdTool (tools/RwdTool) built.
param(
    [Parameter(Mandatory = $true)][string] $RigBuild,
    [Parameter(Mandatory = $true)][string] $RwdTool,
    [string] $Game = 'C:\Program Files (x86)\Steam\steamapps\common\Kohan II'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'RigLauncher.ps1')
if (Get-Process k2 -ErrorAction SilentlyContinue) { throw 'Kohan II is already running; close it first.' }
if ((Get-FileHash (Join-Path $Game 'k2.exe')).Hash -ne '1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45') { throw 'the game is not 1.3.72' }
$shfolder = Join-Path $Game 'shfolder.dll'
if (Test-Path $shfolder) { throw 'the game folder already has a shfolder.dll' }
$work = Join-Path $env:LOCALAPPDATA 'PawsPrecedenceProbe'
$docs = Join-Path $work 'Documents'
$userProbe = Join-Path $docs 'Kohan2\data\pawsprobe'
$gameProbe = Join-Path $Game 'data\pawsprobe'
$archives = Join-Path $Game 'PawsRig'
$startup = Join-Path $Game 'startup\autoexec_zz_pawsprobe.txt'
foreach ($p in $gameProbe, $startup) { if (Test-Path $p) { throw "$p exists already; not touching it" } }
New-Item -ItemType Directory -Force $userProbe, $archives | Out-Null
foreach ($name in 'A', 'B') {
    $src = Join-Path $work "src$name\pawsprobe"
    New-Item -ItemType Directory -Force $src | Out-Null
    "Echo pawsprobe: archive $name`r`nWait 30`r`nReallyQuit`r`n" | Set-Content (Join-Path $src 'order.txt') -Encoding ascii -NoNewline
    & $RwdTool pack (Split-Path $src) (Join-Path $archives "probe$name.rwd") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'RwdTool pack failed' }
}
$logs = Join-Path $Game 'Logs'
$backup = Join-Path $env:LOCALAPPDATA 'PawsMultiplayerRigLogBackup'
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($f in Get-ChildItem $logs -File) { $to = Join-Path $backup $f.Name; if (-not (Test-Path $to) -or (Get-Item $to).Length -ne $f.Length) { Copy-Item $f.FullName $to } }
$settings = "[Vars]`r`n{`r`n`tflag`tResolutionWindowed = true`r`n`tint`tResolutionX = 800`r`n`tint`tResolutionY = 600`r`n`tflag`tMouseClipCursor = false`r`n`tflag`tRunWhenInactive = true`r`n`tfloat`tAudioMainVolume = 0`r`n`tflag`tFirstRun = false`r`n}`r`n"
New-Item -ItemType Directory -Force (Join-Path $docs 'Kohan2\data\User') | Out-Null
[IO.File]::WriteAllText((Join-Path $docs 'Kohan2\data\User\UVars.tgi'), $settings, [Text.Encoding]::ASCII)

function Run-Case($label) {
    $known = @(Get-ChildItem $logs -Filter 'log-*.log' | ForEach-Object Name)
    $id = Start-RigGame $Game '+Exec pawsprobe/order.txt' "K2_RIG_DOCUMENTS=$docs`0"
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p -and -not $p.WaitForExit(150000)) { $p.Kill(); $p.WaitForExit(10000) | Out-Null }
    Start-Sleep 2
    $log = Get-ChildItem $logs -Filter 'log-*.log' | Where-Object { $known -notcontains $_.Name } | Sort-Object LastWriteTime | Select-Object -Last 1
    $text = if ($log) { Get-Content $log.FullName -Raw } else { '' }
    $winner = [regex]::Match($text, 'APP:\s+pawsprobe: ([^\r\n]+)').Groups[1].Value
    if (-not $winner) { $winner = if ($text -match "Could not exec script file") { 'not found by Exec' } else { 'no result' } }
    '{0,-58} -> {1}   ({2})' -f $label, $winner, $(if ($log) { $log.Name } else { 'no log' })
}
try {
    Copy-Item (Join-Path $RigBuild 'shfolder.dll') $shfolder
    "addmod PawsRig/probeA.rwd`r`naddmod PawsRig/probeB.rwd`r`n" | Set-Content $startup -Encoding ascii -NoNewline
    Run-Case 'addmod A, then addmod B'
    "Echo pawsprobe: game data folder`r`nWait 30`r`nReallyQuit`r`n" | Set-Content (Join-Path (New-Item -ItemType Directory -Force $gameProbe) 'order.txt') -Encoding ascii -NoNewline
    Run-Case 'addmod A, addmod B, and <game>\data'
    "Echo pawsprobe: Documents user data`r`nWait 30`r`nReallyQuit`r`n" | Set-Content (Join-Path $userProbe 'order.txt') -Encoding ascii -NoNewline
    Run-Case 'addmod A, addmod B, <game>\data and Documents\Kohan2\data'
    "addmod PawsRig/probeB.rwd`r`naddmod PawsRig/probeA.rwd`r`n" | Set-Content $startup -Encoding ascii -NoNewline
    Remove-Item -LiteralPath (Join-Path $userProbe 'order.txt'); Remove-Item -LiteralPath (Join-Path $gameProbe 'order.txt')
    Run-Case 'addmod B, then addmod A'
    Remove-Item -LiteralPath $startup
    "Echo pawsprobe: game data folder`r`nWait 30`r`nReallyQuit`r`n" | Set-Content (Join-Path $gameProbe 'order.txt') -Encoding ascii -NoNewline
    Run-Case 'no archives, <game>\data only'
} finally {
    foreach ($f in $startup, $shfolder, (Join-Path $gameProbe 'order.txt'), (Join-Path $archives 'probeA.rwd'), (Join-Path $archives 'probeB.rwd')) {
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f }
    }
    foreach ($d in $gameProbe, $archives) { if ((Test-Path $d) -and -not (Get-ChildItem $d)) { Remove-Item -LiteralPath $d } }
}
