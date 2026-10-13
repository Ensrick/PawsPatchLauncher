# Two copies of Kohan II 1.3.72 play a LAN match against each other on one PC, driven by the game's own script
# commands, with the multiplayer recovery module installed in each copy the way the pure helper installs it
# (RigInject.exe). A developer tool: it starts the real game, so never run it while someone is playing. Exit code =
# number of failed checks. See README.md.
#
#   -Scenario SaveCopy    host: LAN game, random map, launch, save once, quit. client: join, play, quit.
#                         Checks the host's save, the client's "(copy)" and both modules' logs.
#   -Scenario Autosave    -AutosaveMinutes for this run; the host plays two intervals. Checks "MP Autosave A/B" on the
#                         host, the client's copies, and that only the host saved.
#   -Scenario Drop        -DropAfter seconds after the client starts, the rig kills the -DropWho copy (like a crash),
#                         or with -DropHow Quit the client leaves from the game's menu. A killed client is started again
#                         and tries to join. Checks what the remaining host's module did.
#   -Scenario Rejoin      like Drop (client killed); the host's module saves "MP Resume", takes everyone back to the lobby
#                         and selects it; the client starts again, joins, gets the save; the host launches again.
#                         Checks that the client is back in a match loaded from "MP Resume".
#   -Scenario HostLeaves  host: LAN lobby, quits once the client is in it. Checks whether the client survives (on 1.3.68
#                         the client crashed in 4 of 18 such runs, after a failed assertion).
#
# Each copy gets its own Documents folder (the stand-in shfolder.dll, K2_RIG_DOCUMENTS) and its own module log under
# %LOCALAPPDATA%\PawsMultiplayerRig; the player's own Documents\Kohan2 is only read. The game folder gets shfolder.dll
# and PawsRig\*.txt for the run; its Logs folder (the game keeps only about ten logs) is copied aside first.
param(
    [Parameter(Mandatory = $true)][string] $RigBuild,  # tools/MultiplayerRig/build.ps1 -OutputDirectory
    [ValidateSet('SaveCopy', 'Autosave', 'Drop', 'Rejoin', 'HostLeaves')] [string] $Scenario = 'SaveCopy',
    [string] $Game = 'C:\Program Files (x86)\Steam\steamapps\common\Kohan II',
    [int] $Port = 5860,                  # the first port the game tries for a LAN host (5860-5869)
    [int] $LaunchAfter = 90,             # seconds the host waits in its lobby for the client before launching
    [int] $SaveAfter = 60,               # SaveCopy: seconds into the match before the host saves
    [string] $SaveName = 'Rig Test',
    [int] $AutosaveMinutes = 1,          # the module's autosave interval for this run (the helper uses 5)
    [ValidateSet('Client', 'Host')] [string] $DropWho = 'Client',
    [ValidateSet('Kill', 'Quit')] [string] $DropHow = 'Kill',
    [int] $DropAfter = 240,              # seconds after the client starts (its match runs from about 120)
    [int] $RelaunchAfter = 300           # Rejoin: seconds after its second launch try before the host launches again
)
$ErrorActionPreference = 'Stop'
$logs = Join-Path $Game 'Logs'
$scripts = Join-Path $Game 'PawsRig'
$playerData = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Kohan2\data'
$rigShfolder = Join-Path $RigBuild 'shfolder.dll'
$inject = Join-Path $RigBuild 'RigInject.exe'
$rigRoot = Join-Path $env:LOCALAPPDATA 'PawsMultiplayerRig'
$copies = [ordered]@{
    host   = Join-Path $rigRoot 'Host'
    client = Join-Path $rigRoot 'Client'
}
function Docs($copy) { Join-Path $copies[$copy] 'Documents' }        # the game adds \Kohan2\data
function Saves($copy) { Join-Path (Docs $copy) 'Kohan2\data\Save' }
function ModuleLog($copy) { Join-Path $copies[$copy] 'paws_multiplayer_recovery_1372.log' }
$shfolderInGame = Join-Path $Game 'shfolder.dll'

if (Get-Process k2 -ErrorAction SilentlyContinue) { throw 'Kohan II is already running; close it first (never run the rig while someone plays).' }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam is not running (the game''s copy protection needs it).' }
foreach ($f in $rigShfolder, $inject) { if (-not (Test-Path $f)) { throw "missing ${f}: run build.ps1 first" } }
if ((Get-FileHash (Join-Path $Game 'k2.exe')).Hash -ne '1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45') { throw 'the game is not 1.3.72 (Steam build 25068126)' }
if ((Test-Path $shfolderInGame) -and (Get-FileHash $shfolderInGame).Hash -ne (Get-FileHash $rigShfolder).Hash) {
    throw 'the game folder has a shfolder.dll that is not the rig''s; not touching it'
}

# The game deletes older logs at start-up: copy them aside first (files already copied are skipped).
$backup = Join-Path $env:LOCALAPPDATA 'PawsMultiplayerRigLogBackup'
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($f in Get-ChildItem $logs -File) {
    $to = Join-Path $backup $f.Name
    if (-not (Test-Path $to) -or (Get-Item $to).Length -ne $f.Length) { Copy-Item $f.FullName $to }
}
"game logs copied to $backup"

# ---------------------------------------------------------------- the copies' own Documents folders
$rigSettings = @'
[Vars]
{
	flag	ResolutionWindowed = true
	flag	ResolutionCoopFullscreenMode = false
	int	ResolutionX = 800
	int	ResolutionY = 600
	flag	MouseClipCursor = false
	flag	RunWhenInactive = true
	float	AudioMainVolume = 0
	flag	FirstRun = false
}
'@
foreach ($copy in $copies.Keys) {
    $data = Join-Path (Docs $copy) 'Kohan2\data'
    # the player's game data (hand-installed mods included, so both copies load the same), not their own files
    robocopy $playerData $data /E /XD Save saves CampaignSave DynamicCampaignSave Films Maps RandomMap Screenshots synclogs User /NFL /NDL /NJH /NJS /NP | Out-Null
    New-Item -ItemType Directory -Force (Join-Path $data 'User'), (Join-Path $data 'Save') | Out-Null
    [IO.File]::WriteAllText((Join-Path $data 'User\UVars.tgi'), $rigSettings.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.Encoding]::ASCII)
    foreach ($old in @(Get-ChildItem (Saves $copy) -Filter '*.RSG' -ErrorAction SilentlyContinue) + @(Get-Item (ModuleLog $copy) -ErrorAction SilentlyContinue)) {
        Remove-Item -LiteralPath $old.FullName
    }
}

# ---------------------------------------------------------------- scripts
$join = @"
RequestMultiplayer
Wait 3
RequestLobbyLAN
Wait 3
LobbySetLANNickname __NICK__
Wait 2
Echo rig: client joining
LobbyJoinDirect
Wait 2
JoinDirectJoin
Wait 10
StagingReady 0
"@
$hostOpen = @"
RequestMultiplayer
Wait 3
RequestLobbyLAN
Wait 3
LobbySetLANNickname RigHost
Wait 2
InternetGameCreate RigGame $Port 2
Wait 3
"@
# No WaitState before the main menu: it crashes the game then. Wait 45 covers start-up.
switch ($Scenario) {
    'SaveCopy' {
        $hostScript = "Wait 45`n$hostOpen`nSelectLaunchType RANDOM_MAP`nEcho rig: host waiting in its lobby`nWait $LaunchAfter`nEcho rig: host launching`nStagingLaunch`nWaitState game`nWait $SaveAfter`nEcho rig: host saving`nGameSave `"Save/$SaveName.RSG`"`nWait 30`nEcho rig: host end`nReallyQuit"
        $clientScript = "Wait 45`n$join`nEcho rig: client waiting`nWaitState game`nEcho rig: client in the match`nWait $($SaveAfter + 90)`nEcho rig: client end`nReallyQuit"
    }
    'Autosave' {
        $inMatch = $AutosaveMinutes * 120 + 45
        $hostScript = "Wait 45`n$hostOpen`nSelectLaunchType RANDOM_MAP`nEcho rig: host waiting in its lobby`nWait $LaunchAfter`nEcho rig: host launching`nStagingLaunch`nWaitState game`nEcho rig: host in the match`nWait $inMatch`nEcho rig: host end`nReallyQuit"
        $clientScript = "Wait 45`n$join`nEcho rig: client waiting`nWaitState game`nEcho rig: client in the match`nWait $($inMatch - 20)`nEcho rig: client end`nReallyQuit"
    }
    { $_ -in 'Drop', 'Rejoin' } {
        # No WaitState here (it gives up after about a minute); the rig drops a copy by the clock instead. A second
        # StagingLaunch covers a client that became ready after the first one.
        $again = if ($Scenario -eq 'Rejoin') { "Wait $RelaunchAfter`nEcho rig: host launching again`nStagingLaunch`nWait 30`nStagingLaunch`nWait 240" } else { 'Wait 420' }
        $hostScript = "Wait 45`n$hostOpen`nSelectLaunchType RANDOM_MAP`nEcho rig: host waiting in its lobby`nWait $LaunchAfter`nEcho rig: host launching`nStagingLaunch`nWait 30`nStagingLaunch`n$again`nEcho rig: host end`nReallyQuit"
        $stay = if ($Scenario -eq 'Drop' -and $DropHow -eq 'Quit') { "Wait $([math]::Max(30, $DropAfter - 75))`nEcho rig: client quitting`nRequestLeaveGame`nWait 20" } else { 'Wait 600' }
        $clientScript = "Wait 45`n$join`nEcho rig: client in the lobby`n$stay`nEcho rig: client end`nReallyQuit"
        $rejoinScript = "Wait 45`n$join`nEcho rig: rejoined client waiting`nWait 40`nStagingReady 0`nWait $(if ($Scenario -eq 'Rejoin') { 420 } else { 110 })`nEcho rig: rejoined client end`nReallyQuit"
    }
    'HostLeaves' {
        $hostScript = "Wait 45`n$hostOpen`nSelectLaunchType RANDOM_MAP`nEcho rig: host waiting in its lobby`nWait 95`nEcho rig: host leaving`nReallyQuit"
        $clientScript = "Wait 45`n$join`nEcho rig: client in the lobby`nWait 150`nEcho rig: client end`nReallyQuit"
    }
}
New-Item -ItemType Directory -Force $scripts | Out-Null
function Write-Script($name, $text) {
    "# rig $name, $Scenario (written by tools/MultiplayerRig/Run-Rig.ps1)`r`n$($text.Replace("`n", "`r`n"))" | Set-Content (Join-Path $scripts "$name.txt") -Encoding ascii
}
Write-Script 'host' $hostScript
Write-Script 'client' $clientScript.Replace('__NICK__', 'RigClient')
if ($rejoinScript) { Write-Script 'rejoin' $rejoinScript.Replace('__NICK__', 'RigClient') }
Copy-Item $rigShfolder $shfolderInGame -Force
$runLimit = switch ($Scenario) { 'Autosave' { $LaunchAfter + $AutosaveMinutes * 120 + 300 } 'HostLeaves' { 420 } 'Drop' { $DropAfter + 420 } 'Rejoin' { $DropAfter + 720 } default { $LaunchAfter + $SaveAfter + 300 } }

# ---------------------------------------------------------------- starting a copy
. (Join-Path $PSScriptRoot 'RigLauncher.ps1')
function Log-Names { @(Get-ChildItem $logs -Filter 'log-*.log' | ForEach-Object Name) }
function Wait-NewLog($known, $seconds) {
    $t = Get-Date
    while (((Get-Date) - $t).TotalSeconds -lt $seconds) {
        $new = Get-ChildItem $logs -Filter 'log-*.log' | Where-Object { $_.Name -match '^log-\d+\.log$' -and $known -notcontains $_.Name } |
            Sort-Object { [int]($_.Name -replace '\D', '') } -Descending | Select-Object -First 1
        if ($new) { return $new.FullName }
        Start-Sleep 1
    }
    throw 'no new game log appeared'
}
# Starts a copy with its script, parks its window, installs the module; returns @{ Id; Log } (status on the host).
function Start-Copy($copy, $script) {
    $known = Log-Names
    $envText = "K2_RIG_DOCUMENTS=$(Docs $copy)`0"
    if ($copy -ne 'host') { $envText += "K2_RIG_JOIN=127.0.0.1:$Port`0" }
    $id = Start-RigGame $Game "+Exec PawsRig/$script.txt" $envText
    $log = Wait-NewLog $known 60
    $result = & $inject $id $copies[$copy] $AutosaveMinutes 2>&1
    Write-Host "$copy ($script): process $id, log $(Split-Path $log -Leaf); $($result -join ' ')"
    if ($LASTEXITCODE -ne 0) { throw "the module was not installed in the $copy copy" }
    foreach ($i in 1..20) { Start-Sleep -Milliseconds 500; $null = [PawsRig.Launcher]::Park($id) }
    @{ Id = $id; Log = $log }
}

$hostCopy = $clientCopy = $rejoinCopy = $null
$dropped = $null
try {
    $hostCopy = Start-Copy 'host' 'host'
    # The game buffers its log: readiness comes from Windows, the host listening on its port.
    $t0 = Get-Date
    while (-not (Get-NetUDPEndpoint -LocalPort $Port -ErrorAction SilentlyContinue | Where-Object OwningProcess -eq $hostCopy.Id)) {
        if (((Get-Date) - $t0).TotalSeconds -gt 240 -or -not (Get-Process -Id $hostCopy.Id -ErrorAction SilentlyContinue)) { throw 'the host never opened its game' }
        Start-Sleep 1
    }
    'host: game created'
    $clientCopy = Start-Copy 'client' 'client'
    $t0 = Get-Date
    while ((Get-Process -Id (@($hostCopy.Id, $clientCopy.Id, $(if ($rejoinCopy) { $rejoinCopy.Id })) | Where-Object { $_ }) -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt $runLimit) {
        if ($Scenario -eq 'Drop' -and $DropHow -eq 'Quit' -and -not $dropped -and ((Get-Date) - $t0).TotalSeconds -ge $DropAfter) {
            $dropped = Get-Date; 'drop: the client is quitting from the menu about now'
        }
        if ($Scenario -in 'Drop', 'Rejoin' -and $DropHow -eq 'Kill' -and -not $dropped -and ((Get-Date) - $t0).TotalSeconds -ge $DropAfter) {
            $victim = if ($DropWho -eq 'Host' -and $Scenario -eq 'Drop') { $hostCopy.Id } else { $clientCopy.Id }
            Stop-Process -Id $victim -Force -ErrorAction SilentlyContinue
            $dropped = Get-Date
            "drop: killed the $($DropWho.ToLower()) (process $victim) $([math]::Round(($dropped - $t0).TotalSeconds)) s after the client started"
        }
        if (($Scenario -eq 'Rejoin' -or ($Scenario -eq 'Drop' -and $DropWho -eq 'Client' -and $DropHow -eq 'Kill')) -and $dropped -and -not $rejoinCopy -and ((Get-Date) - $dropped).TotalSeconds -ge 20) {
            $rejoinCopy = Start-Copy 'client' 'rejoin'
            'drop: client started again'
        }
        Start-Sleep -Milliseconds 250
    }
} finally {
    # A copy can take minutes to close after ReallyQuit; the game folder must not keep the test files.
    $ours = @($hostCopy, $clientCopy, $rejoinCopy | Where-Object { $_ } | ForEach-Object { $_.Id })
    $exitBy = (Get-Date).AddSeconds(300)
    while ($ours -and (Get-Process -Id $ours -ErrorAction SilentlyContinue) -and (Get-Date) -lt $exitBy) { Start-Sleep -Seconds 2 }
    foreach ($id in $ours) {
        # only the rig's own copies: started by this run, with a rig script on their command line
        $p = Get-CimInstance Win32_Process -Filter "ProcessId=$id" -ErrorAction SilentlyContinue
        if ($p -and $p.Name -eq 'k2.exe' -and $p.CommandLine -match 'PawsRig/\w+\.txt') { Stop-Process -Id $id -Force -ErrorAction SilentlyContinue; "stopped the rig's copy $id (still running after the wait)" }
    }
    Start-Sleep -Seconds 3
    if (-not (Get-Process k2 -ErrorAction SilentlyContinue)) {
        if (Test-Path $shfolderInGame) { Remove-Item -LiteralPath $shfolderInGame; 'test shfolder.dll removed from the game folder' }
        foreach ($f in Get-ChildItem $scripts -Filter '*.txt' -ErrorAction SilentlyContinue) { Remove-Item -LiteralPath $f.FullName }
        if ((Test-Path $scripts) -and -not (Get-ChildItem $scripts)) { Remove-Item -LiteralPath $scripts }
    } else { 'a copy of the game is still running: shfolder.dll and the rig scripts stay until it exits' }
}

# ---------------------------------------------------------------- report
function Final-Log($path) { if ($path) { Get-ChildItem $logs -Filter (([IO.Path]::GetFileNameWithoutExtension($path)) + '*.log') | Select-Object -First 1 } }
function Module-Text($copy) { $p = ModuleLog $copy; if (Test-Path $p) { Get-Content -LiteralPath $p -Raw } else { '' } }
foreach ($copy in $copies.Keys) { "--- $copy module log"; Module-Text $copy }
$hostLog = Final-Log $hostCopy.Log
$hostText = if ($hostLog) { Get-Content $hostLog.FullName -Raw } else { '' }
$checks = [ordered]@{ 'client joined the host' = $hostText.Contains('RigClient has joined the game') }
$checks['host module installed'] = (Module-Text 'host').Contains('installed for k2.exe 1.3.72')
$checks['client module installed'] = (Module-Text 'client').Contains('installed for k2.exe 1.3.72')
switch ($Scenario) {
    'SaveCopy' {
        '--- saves'
        $save = Join-Path (Saves 'host') "$SaveName.RSG"; $copy = Join-Path (Saves 'client') "$SaveName (copy).RSG"
        foreach ($p in $save, $copy) { if (Test-Path $p) { '{0}: {1} bytes, sha256 {2}' -f (Split-Path $p -Leaf), (Get-Item $p).Length, (Get-FileHash $p).Hash.ToLower() } else { "$(Split-Path $p -Leaf): missing" } }
        $checks['host saved'] = Test-Path $save
        $checks['client wrote its copy'] = Test-Path $copy
        $checks['client module logged the copy'] = (Module-Text 'client') -match 'save copy ".*" written'
        $checks['host module reported its save'] = (Module-Text 'host') -match 'host save ".*": \d+ bytes, lobby checksum'
    }
    'Autosave' {
        foreach ($n in 'MP Autosave A', 'MP Autosave B') {
            $checks["host saved $n"] = Test-Path (Join-Path (Saves 'host') "$n.RSG")
            $checks["client wrote $n (copy)"] = Test-Path (Join-Path (Saves 'client') "$n (copy).RSG")
        }
        $checks['host module saved twice'] = ([regex]::Matches((Module-Text 'host'), 'autosave: saving')).Count -ge 2
        $checks['client module never saved'] = -not (Module-Text 'client').Contains('autosave: saving')
    }
    { $_ -in 'Drop', 'Rejoin' } {
        $pattern = 'Timeout\(\)|CSD|Vote|PlayerLeave\(\)|already in progress|Rejected|taken over|BackupMode|Becoming|has joined|quit the game|GamePause|GameSave|MP Resume|Loading File|SetNewState|has ended|ASSERT|exception|rig: '
        foreach ($pair in @(@('host', $hostCopy.Log), @('client', $clientCopy.Log), @('rejoined client', $(if ($rejoinCopy) { $rejoinCopy.Log })))) {
            $f = Final-Log $pair[1]; if (-not $f) { continue }
            "--- $($pair[0]) ($($f.Name))"
            Select-String -Path $f.FullName -Pattern $pattern | Where-Object { $_.Line -notmatch 'called with invalid player|^: \[time [0-9.]+\] \(' } |
                Select-Object -First 60 | ForEach-Object { '{0,6}: {1}' -f $_.LineNumber, $_.Line.Trim().Substring(0, [Math]::Min(170, $_.Line.Trim().Length)) }
        }
        $checks['the rig dropped a copy'] = [bool]$dropped
        if ($Scenario -eq 'Drop' -and $DropHow -eq 'Quit') {
            $checks['the host module saw a quit, not a drop'] = (Module-Text 'host').Contains('quit the match on purpose')
            $checks['the host module did not save'] = -not (Module-Text 'host').Contains('saving "Save/MP Resume')
        } else {
            $saver = if ($DropWho -eq 'Host' -and $Scenario -eq 'Drop') { 'client' } else { 'host' }
            $checks["the $saver module saved and paused"] = (Module-Text $saver) -match 'drop recovery: .* saving "Save/MP Resume'
            $checks['"MP Resume" was written'] = [bool](Get-ChildItem (Saves $saver) -Filter 'MP Resume *.RSG' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*(copy)*' -and $_.LastWriteTime -gt $dropped })
        }
        if ($Scenario -eq 'Rejoin') {
            $checks['host module took everyone to the lobby with "MP Resume"'] = (Module-Text 'host').Contains('in the lobby with "MP Resume" selected')
            $rj = if ($rejoinCopy) { Final-Log $rejoinCopy.Log } else { $null }
            $rjText = if ($rj) { Get-Content $rj.FullName -Raw } else { '' }
            $checks['the dropped client joined again'] = $rjText -and -not $rjText.Contains('Game is already in progress') -and $rjText.Contains('SetServer() Becoming client')
            $checks['the dropped client got "MP Resume"'] = [bool](Get-ChildItem (Saves 'client') -Filter 'MP Resume *.RSG' -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*(copy)*' -and $_.LastWriteTime -gt $dropped })
            $checks['the dropped client is back in a match from "MP Resume"'] = $rjText.Contains('Loading File: <Save/MP Resume ')
        }
    }
    'HostLeaves' {
        $clientLog = Final-Log $clientCopy.Log
        $text = if ($clientLog) { Get-Content $clientLog.FullName -Raw } else { '' }
        "client: $(([regex]::Matches($text, 'ASSERT: ')).Count) failed assertion(s), log $(if ($clientLog) { $clientLog.Name } else { 'missing' })"
        $checks['client took over when the host left'] = $text.Contains('has taken over as host')
        $checks['client did not crash'] = $clientLog -and $clientLog.Name -like '*-ok.log' -and -not $text.Contains('timeout waiting for mutex')
    }
}
'--- checks'
$fail = 0
foreach ($c in $checks.GetEnumerator()) { '{0} {1}' -f $(if ($c.Value) { 'ok  ' } else { 'FAIL' }), $c.Key; if (-not $c.Value) { $fail++ } }
"game logs: host $(Split-Path $hostCopy.Log -Leaf), client $(Split-Path $clientCopy.Log -Leaf)"
exit $fail
