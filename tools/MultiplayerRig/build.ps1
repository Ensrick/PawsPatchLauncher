# Builds the test rig into -OutputDirectory: the stand-in shfolder.dll, the multiplayer recovery module (with its own
# checks, game/multiplayer-recovery/build.ps1) and RigInject.exe, which installs that module into a running copy the
# way the pure helper does. Never starts the game.
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$NativeCompiler,
    [string]$MappedImage
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
& $NativeCompiler -shared -Wall -Werror (Join-Path $PSScriptRoot 'rig_shfolder.c') -o (Join-Path $OutputDirectory 'shfolder.dll') -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Rig shfolder.dll build failed.' }
$native = Join-Path $OutputDirectory 'native'
$module = @{ OutputDirectory = $native; NativeCompiler = $NativeCompiler }
if ($MappedImage) { $module.MappedImage = $MappedImage }
& (Join-Path $repo 'game\multiplayer-recovery\build.ps1') @module
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll "/out:$(Join-Path $OutputDirectory 'RigInject.exe')" `
    "/resource:$(Join-Path $native 'paws_multiplayer_recovery.dll'),PawMultiplayerRecoveryNative" `
    (Join-Path $repo 'game\multiplayer-recovery\MultiplayerRecovery.cs') (Join-Path $PSScriptRoot 'RigInject.cs')
if ($LASTEXITCODE -ne 0) { throw 'RigInject build failed.' }
"rig built in $OutputDirectory"
