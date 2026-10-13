# Builds paws_multiplayer_recovery.dll into the native artifact folder that game/pure-fixes/build_beta.py embeds
# (--native), and runs its checks. Never starts or installs the game.
#   -NativeCompiler   x86 TCC, as for the lobby compatibility module
#   -MappedImage      optional: the hash-pinned 1.3.72 analysis snapshot (headers and sections at their RVAs); every
#                     signature and call site the module relies on is then checked against it
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$NativeCompiler,
    [string]$MappedImage
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'multiplayer_recovery.c'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$dll = Join-Path $OutputDirectory 'paws_multiplayer_recovery.dll'
& $NativeCompiler -shared -Wall -Werror $source -o $dll -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer recovery DLL build failed.' }
$tests = Join-Path $OutputDirectory 'MultiplayerRecoveryTests.exe'
& $NativeCompiler -Wall -Werror -DPAW_TEST $source -o $tests -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer recovery test build failed.' }
& $tests
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer recovery checks failed.' }
if ($MappedImage) {
    & $tests $MappedImage
    if ($LASTEXITCODE -ne 0) { throw 'Multiplayer recovery image checks failed.' }
}
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$installerTests = Join-Path $OutputDirectory 'MultiplayerRecoveryInstallerTests.exe'
& $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll "/out:$installerTests" (Join-Path $PSScriptRoot 'MultiplayerRecovery.cs') (Join-Path $PSScriptRoot 'InstallerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer recovery installer test compilation failed.' }
& $installerTests
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer recovery installer checks failed.' }
