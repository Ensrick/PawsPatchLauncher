param(
    [Parameter(Mandatory=$true)][string]$Stock,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$NativeCompiler,
    [string]$MappedImage
)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'RigSafety.ps1')
$root=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
$stockRoot=[IO.Path]::GetFullPath($Stock).TrimEnd('\')
if ($root -eq $stockRoot -or $root.StartsWith($stockRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $stockRoot.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Choose an output directory separate from the installed game.' }
if (Test-Path -LiteralPath $root) { throw 'Choose a new output directory. Previous test evidence is never overwritten.' }
if ((Get-FileHash -LiteralPath (Join-Path $stockRoot 'k2.exe')).Hash -ne '1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45') { throw 'Requires Steam Kohan II 1.3.72.' }
$game=Join-Path $root 'game'
$build=Join-Path $root 'build'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
New-Item -ItemType Directory -Path $game,(Join-Path $game 'Logs'),(Join-Path $root 'runs') | Out-Null
# Explicit stock runtime list excludes local graphics proxies, helper EXEs and loose mods.
$stockFiles=@('k2.exe','Data.rwd','Music.rwd','BinkW32.dll','mss32.dll','steam_api.dll','avifil32.dll','D3DCompiler_43.dll','D3DX9_43.dll','DbgHelp.dll','GdiPlus.dll','iqpfx.dll','UnicoWS.dll')
$manifest=[ordered]@{}
foreach ($name in $stockFiles) {
    $from=Join-Path $stockRoot $name
    Copy-Item -LiteralPath $from -Destination (Join-Path $game $name)
    $digest=(Get-FileHash -LiteralPath $from).Hash
    if ((Get-FileHash -LiteralPath (Join-Path $game $name)).Hash -ne $digest) { throw "Copy changed: $name" }
    $manifest[$name]=$digest
}
Copy-Item -LiteralPath (Join-Path $stockRoot 'mss') -Destination (Join-Path $game 'mss') -Recurse
# Use the project's pinned Steam loose files and skin hashes, not modified installed data.
$baseline=Get-Content (Join-Path $repo 'src\PawsPatchLauncher\SteamVanillaBaseline.cs') -Raw
foreach ($match in [regex]::Matches($baseline,'NormalizeRelativePath\("([^"]+)"\)\]\s*=\s*"([A-F0-9]{64})"')) {
    $relative=$match.Groups[1].Value; $expected=$match.Groups[2].Value
    $from=if ($relative.EndsWith('.rwd')) { Join-Path $stockRoot $relative } else { Join-Path (Join-Path $repo 'src\PawsPatchLauncher\Assets\Vanilla1372') (Split-Path $relative -Leaf) }
    if ((Get-FileHash -LiteralPath $from).Hash -ne $expected) { throw "Stock resource mismatch: $relative" }
    $to=Join-Path $game $relative
    New-Item -ItemType Directory -Path (Split-Path $to) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to
    $manifest[$relative]=$expected
}
if ($manifest.Count -ne $stockFiles.Count+8) { throw 'Stock baseline list could not be verified.' }
@{schema=1;game=$game;source=$stockRoot;files=$manifest} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $game '.paws-multiplayer-fixture.json') -Encoding UTF8
Assert-RigFixture $game
$argsBuild=@{OutputDirectory=$build;NativeCompiler=$NativeCompiler}
if ($MappedImage) {$argsBuild.MappedImage=$MappedImage}
& (Join-Path $PSScriptRoot 'build.ps1') @argsBuild
@{game=$game;build=$build;root=$root;source=$stockRoot} | ConvertTo-Json | Set-Content (Join-Path $root 'rig.json') -Encoding UTF8
$startScript=Join-Path $PSScriptRoot 'Start-Tests.ps1'
@('@echo off',('powershell.exe -NoProfile -ExecutionPolicy Bypass -File "'+$startScript+'" -Configuration "%~dp0rig.json"'),'pause') | Set-Content -LiteralPath (Join-Path $root 'Start multiplayer tests.cmd') -Encoding ASCII
Write-Host "Prepared isolated multiplayer rig: $root"
