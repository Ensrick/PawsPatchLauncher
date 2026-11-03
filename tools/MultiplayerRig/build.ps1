# Builds the test rig into -OutputDirectory: the stand-in shfolder.dll, the multiplayer saves module (with its own
# checks, game/multiplayer-saves/build.ps1) and RigInject.exe, which installs that module into a running copy the
# way the pure helper does. Never starts the game.
param(
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][string]$NativeCompiler,
    [string]$MappedImage
)
$ErrorActionPreference = 'Stop'
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
& $NativeCompiler -shared -Wall -Werror (Join-Path $PSScriptRoot 'rig_shfolder.c') -o (Join-Path $OutputDirectory 'shfolder.dll') -lkernel32
if ($LASTEXITCODE -ne 0) { throw 'Rig shfolder.dll build failed.' }
$native = Join-Path $OutputDirectory 'native'
$module = @{ OutputDirectory = $native; NativeCompiler = $NativeCompiler }
if ($MappedImage) { $module.MappedImage = $MappedImage }
& (Join-Path $repo 'game\multiplayer-saves\build.ps1') @module
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll "/out:$(Join-Path $OutputDirectory 'RigInject.exe')" `
    "/resource:$(Join-Path $native 'paws_multiplayer_saves.dll'),PawMultiplayerSavesNative" `
    (Join-Path $repo 'game\multiplayer-saves\MultiplayerSaves.cs') (Join-Path $PSScriptRoot 'RigInject.cs')
if ($LASTEXITCODE -ne 0) { throw 'RigInject build failed.' }
& $compiler /nologo /target:exe /platform:x86 "/out:$(Join-Path $OutputDirectory 'FolderTests.exe')" (Join-Path $PSScriptRoot 'FolderTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Folder test build failed.' }
& (Join-Path $OutputDirectory 'FolderTests.exe') (Join-Path $OutputDirectory 'shfolder.dll') (Join-Path $OutputDirectory 'folder-test-profile')
if ($LASTEXITCODE -ne 0) { throw 'Private folder checks failed.' }
"rig built in $OutputDirectory"
