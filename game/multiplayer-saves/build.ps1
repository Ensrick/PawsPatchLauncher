# Builds paws_multiplayer_saves.dll for game/pure-fixes/build_beta.py --saves-native,
# and runs its checks. Never starts or installs the game.
#   -NativeCompiler   x86 TCC, as for the lobby compatibility module
#   -MappedImage      optional: the hash-pinned 1.3.72 analysis snapshot (headers and sections at their RVAs); every
#                     signature and call site the module relies on is then checked against it
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$NativeCompiler,
    [string]$MappedImage
)
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot 'multiplayer_saves.c'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$dll = Join-Path $OutputDirectory 'paws_multiplayer_saves.dll'
& $NativeCompiler -shared -Wall -Werror $source -o $dll -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer saves DLL build failed.' }
$tests = Join-Path $OutputDirectory 'MultiplayerSavesTests.exe'
& $NativeCompiler -Wall -Werror -DPAW_TEST $source -o $tests -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer saves test build failed.' }
& $tests
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer saves checks failed.' }
if ($MappedImage) {
    & $tests $MappedImage
    if ($LASTEXITCODE -ne 0) { throw 'Multiplayer saves image checks failed.' }
}
$fastTests = Join-Path $OutputDirectory 'FastSaveTests.exe'
& $NativeCompiler -Wall -Werror (Join-Path $PSScriptRoot 'fast_save_tests.c') -o $fastTests -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Fast save test build failed.' }
& $fastTests
if ($LASTEXITCODE -ne 0) { throw 'Fast save completion checks failed.' }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$installerTests = Join-Path $OutputDirectory 'MultiplayerSavesInstallerTests.exe'
& $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll "/out:$installerTests" (Join-Path $PSScriptRoot 'MultiplayerSaves.cs') (Join-Path $PSScriptRoot 'InstallerTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer saves installer test compilation failed.' }
& $installerTests
if ($LASTEXITCODE -ne 0) { throw 'Multiplayer saves installer checks failed.' }
