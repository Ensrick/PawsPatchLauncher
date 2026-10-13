# Two copies of Kohan II 1.3.72 play a LAN match against each other on one PC, driven by the game's own script
# commands, with the multiplayer saves module installed in each copy the way the pure helper installs it
# (RigInject.exe). A developer tool: it starts the real game, so never run it while someone is playing. Exit code =
# number of failed checks. See README.md.
#
#   -Scenario SaveCopy    host: LAN game, random map, launch, save once, quit. client: join, play, quit.
#                         Checks the host's save, the client's "(copy)" and both modules' logs.
#   -Scenario Autosave    -AutosaveMinutes for this run; the host plays two intervals. Checks "MP Autosave A/B" on the
#                         host, the client's copies, and that only the host saved.
#   -Scenario LoadCopy    the host hosts the match again from a player's save copy (-LoadSave, e.g. the client's
#                         "Rig Test (copy).RSG" from a SaveCopy run, copied in as the host's only save); the client
#                         joins without it and receives it; both launch. Checks that both are in a match from it,
#                         that the client skips its copy of the host's autosave (a player who received the save
#                         cannot save it on 1.3.72), and that it takes over without crashing when the host quits.
#   -Scenario HostLeaves  host: LAN lobby, quits once the client is in it. Checks whether the client survives (on 1.3.68
#                         the client crashed in 4 of 18 such runs, after a failed assertion).
#
# Each copy gets its own Documents folder (the stand-in shfolder.dll, K2_RIG_DOCUMENTS) and its own module log under
# %LOCALAPPDATA%\PawsMultiplayerRig; the player's own Documents\Kohan2 is only read. The game folder gets shfolder.dll
# and PawsRig\host.txt and client.txt for the run; the rig refuses to start if any of them exists already, and removes
# only those afterwards. Its Logs folder (the game keeps only about ten logs) is copied aside first.
param(
    [Parameter(Mandatory = $true)][string] $RigBuild,  # tools/MultiplayerRig/build.ps1 -OutputDirectory
    [ValidateSet('SaveCopy', 'Autosave', 'LoadCopy', 'HostLeaves')] [string] $Scenario = 'SaveCopy',
    [string] $Game = 'C:\Program Files (x86)\Steam\steamapps\common\Kohan II',
    [int] $Port = 5860,                  # the first port the game tries for a LAN host (5860-5869)
    [int] $LaunchAfter = 90,             # seconds the host waits in its lobby for the client before launching
    [int] $SaveAfter = 60,               # SaveCopy: seconds into the match before the host saves
    [string] $SaveName = 'Rig Test',
    [string] $LoadSave,                  # LoadCopy: the save the host loads (copied into its Save folder)
    [int] $AutosaveMinutes = 1,          # the module's autosave interval for this run (the helper uses 5)
    [switch] $TestSetupFailure           # developer check of the rig itself: fail right after the host copy starts
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
function ModuleLog($copy) { Join-Path $copies[$copy] 'Logs\paws_recovery.log' }
$shfolderInGame = Join-Path $Game 'shfolder.dll'
$ownFiles = @($shfolderInGame) + @('host', 'client' | ForEach-Object { Join-Path $scripts "$_.txt" })

if (Get-Process k2 -ErrorAction SilentlyContinue) { throw 'Kohan II is already running; close it first (never run the rig while someone plays).' }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { throw 'Steam is not running (the game''s copy protection needs it).' }
foreach ($f in $rigShfolder, $inject) { if (-not (Test-Path $f)) { throw "missing ${f}: run build.ps1 first" } }
if ((Get-FileHash (Join-Path $Game 'k2.exe')).Hash -ne '1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45') { throw 'the game is not 1.3.72 (Steam build 25068126)' }
# Every path the run creates in the game folder must be new: never overwrite or remove a file the rig did not create
# in this run (a leftover from an interrupted run included: check it and remove it by hand).
foreach ($f in $ownFiles) { if (Test-Path -LiteralPath $f) { throw "$f exists already; not touching it" } }
$createdScripts = -not (Test-Path $scripts)

# The game deletes older logs at start-up: copy them aside first (files already copied are skipped).
$backup = Join-Path $env:LOCALAPPDATA 'PawsMultiplayerRigLogBackup'
New-Item -ItemType Directory -Force $backup | Out-Null
foreach ($f in Get-ChildItem $logs -File) {
    $to = Join-Path $backup $f.Name
    if (-not (Test-Path $to) -or (Get-Item $to).Length -ne $f.Length) { Copy-Item $f.FullName $to }
}
"game logs copied to $backup"

# LoadCopy: read the save before anything below empties the rig's Save folders (it may be a copy from an earlier run
# in one of them).
$loadLeaf = $loadBytes = $null
if ($Scenario -eq 'LoadCopy') {
    if (-not $LoadSave -or -not (Test-Path -LiteralPath $LoadSave -PathType Leaf) -or [IO.Path]::GetExtension($LoadSave) -ne '.rsg') {
        throw 'LoadCopy needs -LoadSave <a .RSG file>'
    }
    $loadLeaf = Split-Path $LoadSave -Leaf
    $loadBytes = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $LoadSave).ProviderPath)
    if ($loadBytes.Length -eq 0) { throw "$LoadSave is empty" }
}

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

if ($Scenario -eq 'LoadCopy') { [IO.File]::WriteAllBytes((Join-Path (Saves 'host') $loadLeaf), $loadBytes) }

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
        'LoadCopy' {
        # The load dialog's Commit takes the save its list selects first: the host has only the copy.
        $hostScript = "Wait 45`n$hostOpen`nSelectLaunchType LOAD_SAVED_GAME`nWait 3`nCommit`nEcho rig: host waiting in its lobby`nWait $($LaunchAfter + 150)`nEcho rig: host launching`nStagingLaunch`nWait 30`nStagingLaunch`nWait 90`nEcho rig: host end`nReallyQuit"
        $clientScript = "Wait 45`n$join`nEcho rig: client waiting`nWait 40`nStagingReady 0`nWait $($LaunchAfter + 230)`nEcho rig: client end`nReallyQuit"
    }
    'HostLeaves' {
        $hostScript = "Wait 45`n$hostOpen`nSelectLaunchType RANDOM_MAP`nEcho rig: host waiting in its lobby`nWait 95`nEcho rig: host leaving`nReallyQuit"
        $clientScript = "Wait 45`n$join`nEcho rig: client in the lobby`nWait 150`nEcho rig: client end`nReallyQuit"
    }
}
New-Item -ItemType Directory -Force $scripts | Out-Null
# CR LF once per line, whatever line ends this file has: a checkout with CR LF puts them into the here-strings above,
# and the game reads a doubled CR as part of the command ("Unknown command: RequestMultiplayer").
function Write-Script($name, $text) {
    "# rig $name, $Scenario (written by tools/MultiplayerRig/Run-Rig.ps1)`r`n$($text.Replace("`r`n", "`n").Replace("`n", "`r`n"))" | Set-Content (Join-Path $scripts "$name.txt") -Encoding ascii
}
Write-Script 'host' $hostScript
Write-Script 'client' $clientScript.Replace('__NICK__', 'RigClient')
Copy-Item $rigShfolder $shfolderInGame
$runLimit = switch ($Scenario) { 'Autosave' { $LaunchAfter + $AutosaveMinutes * 120 + 300 } 'HostLeaves' { 420 } 'LoadCopy' { $LaunchAfter + 600 } default { $LaunchAfter + $SaveAfter + 300 } }

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
    if ($TestSetupFailure) { throw "injected setup failure after starting the $copy copy (-TestSetupFailure)" }
    $log = Wait-NewLog $known 60
    $result = & $inject $id $copies[$copy] $AutosaveMinutes 2>&1
    Write-Host "$copy ($script): process $id, log $(Split-Path $log -Leaf); $($result -join ' ')"
    if ($LASTEXITCODE -ne 0) { throw "the module was not installed in the $copy copy" }
    foreach ($i in 1..20) { Start-Sleep -Milliseconds 500; $null = [PawsRig.Launcher]::Park($id) }
    @{ Id = $id; Log = $log }
}

$hostCopy = $clientCopy = $null
$completed = $false
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
    while ((Get-Process -Id $hostCopy.Id, $clientCopy.Id -ErrorAction SilentlyContinue) -and ((Get-Date) - $t0).TotalSeconds -lt $runLimit) {
        Start-Sleep -Milliseconds 250
    }
    $completed = $true
} finally {
    # Only the copies this run started (recorded by Start-RigGame). After a normal run a copy can take minutes to close
    # after ReallyQuit; after a failed setup they are stopped at once. The game folder must not keep the test files.
    $exitBy = (Get-Date).AddSeconds($(if ($completed) { 300 } else { 0 }))
    while (@(Get-RigCopies).Count -and (Get-Date) -lt $exitBy) { Start-Sleep -Seconds 2 }
    foreach ($id in @(Get-RigCopies)) {
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
        "stopped the rig's copy $id ($(if ($completed) { 'still running after the wait' } else { 'the run did not complete' }))"
    }
    Start-Sleep -Seconds 3
    if (-not (Get-Process k2 -ErrorAction SilentlyContinue)) {
        foreach ($f in $ownFiles) { if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f } }
        if ($createdScripts -and (Test-Path $scripts) -and -not (Get-ChildItem $scripts)) { Remove-Item -LiteralPath $scripts }
        'the rig''s files removed from the game folder'
    } else { 'a copy of the game is still running: shfolder.dll and the rig scripts stay until it exits' }
}

# ---------------------------------------------------------------- report
function Final-Log($path) { if ($path) { Get-ChildItem $logs -Filter (([IO.Path]::GetFileNameWithoutExtension($path)) + '*.log') | Select-Object -First 1 } }
function Module-Text($copy) { $p = ModuleLog $copy; if (Test-Path $p) { Get-Content -LiteralPath $p -Raw } else { '' } }
foreach ($copy in $copies.Keys) { "--- $copy module log"; Module-Text $copy }
$hostLog = Final-Log $hostCopy.Log
$hostText = if ($hostLog) { Get-Content $hostLog.FullName -Raw } else { '' }
$clientLog = Final-Log $clientCopy.Log
$clientText = if ($clientLog) { Get-Content $clientLog.FullName -Raw } else { '' }
# A peer survived the run: its log was closed normally (renamed -ok, not -crash) and has no failed assertion, desync,
# unhandled exception or lock timeout.
$markers = 'ASSERT: |out of sync|OUT OF SYNC|desync|unhandled exception|caught C\+\+ exception|timeout waiting for mutex'
function Survived($log, $text) { $log -and $log.Name -like '*-ok.log' -and $text -notmatch $markers }
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
        $checks['both peers survived'] = (Survived $hostLog $hostText) -and (Survived $clientLog $clientText)
    }
    'Autosave' {
        foreach ($n in 'MP Autosave A', 'MP Autosave B') {
            $checks["host saved $n"] = Test-Path (Join-Path (Saves 'host') "$n.RSG")
            $checks["client wrote $n (copy)"] = Test-Path (Join-Path (Saves 'client') "$n (copy).RSG")
        }
        $checks['host module saved twice'] = ([regex]::Matches((Module-Text 'host'), 'autosave: saving')).Count -ge 2
        $checks['host module saw both saves end'] = ([regex]::Matches((Module-Text 'host'), 'autosave "Save/MP Autosave [AB]\.RSG": \d+ bytes')).Count -ge 2
        $checks['client module never saved'] = -not (Module-Text 'client').Contains('autosave: saving')
        $checks['both peers survived'] = (Survived $hostLog $hostText) -and (Survived $clientLog $clientText)
    }
    'LoadCopy' {
        $leaf = Split-Path $LoadSave -Leaf
        foreach ($pair in @(@('host', $hostText), @('client', $clientText))) {
            $at = $pair[1].LastIndexOf("Loading File: <Save/$leaf>")
            $checks["the $($pair[0]) is in a match loaded from the copy"] = $at -ge 0 -and $pair[1].IndexOf('ChangeToState(2,', $at) -gt 0
        }
        $checks['the client received the copy'] = Test-Path (Join-Path (Saves 'client') $leaf)
        $clientModule = Module-Text 'client'
        if ((Module-Text 'host') -match 'autosave: saving') {
            $checks['the client skipped its copy (the game cannot save here)'] = $clientModule -match 'save copy of .* skipped: .* is not loaded'
        }
        $checks['the client took over when the host left'] = $clientText.Contains('has taken over as host')
        $checks['the client module never autosaved the match it cannot save'] = -not $clientModule.Contains('autosave: saving')
        $checks['both peers survived'] = (Survived $hostLog $hostText) -and (Survived $clientLog $clientText)
    }
    'HostLeaves' {
        "client: $(([regex]::Matches($clientText, 'ASSERT: ')).Count) failed assertion(s), log $(if ($clientLog) { $clientLog.Name } else { 'missing' })"
        $checks['client took over when the host left'] = $clientText.Contains('has taken over as host')
        $checks['client survived'] = Survived $clientLog $clientText
    }
}
'--- checks'
$fail = 0
foreach ($c in $checks.GetEnumerator()) { '{0} {1}' -f $(if ($c.Value) { 'ok  ' } else { 'FAIL' }), $c.Key; if (-not $c.Value) { $fail++ } }
"game logs: host $(Split-Path $hostCopy.Log -Leaf), client $(Split-Path $clientCopy.Log -Leaf)"
exit $fail
