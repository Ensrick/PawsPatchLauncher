param([Parameter(Mandatory=$true)][string]$BaselineBuild,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
if(Test-Path -LiteralPath $OutputDirectory){throw 'Use a new candidate build directory.'}
$repo=Resolve-Path (Join-Path $PSScriptRoot '../..')
New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
Copy-Item -LiteralPath (Join-Path $BaselineBuild 'shfolder.dll') -Destination $OutputDirectory
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /define:PAW_TASK_STATUS,PAW_PURE_EXTENDED `
    "/out:$(Join-Path $OutputDirectory 'RigInject.exe')" `
    "/resource:$(Join-Path $BaselineBuild 'native/paws_multiplayer_saves.dll'),PawMultiplayerSavesNative" `
    (Join-Path $repo 'game/multiplayer-saves/MultiplayerSaves.cs') (Join-Path $PSScriptRoot 'RigInject.cs') `
    (Join-Path $PSScriptRoot 'TaskStatusRigContracts.cs') `
    (Join-Path $repo 'game/pure-fixes/NativeMemory.cs') (Join-Path $repo 'game/pure-fixes/PurePatch.cs') `
    (Join-Path $repo 'game/engine-crash-fixes/TaskStatusPatch.cs')
if($LASTEXITCODE -ne 0){throw 'Candidate rig build failed.'}
$sources=@{}
foreach($path in @('game/multiplayer-saves/MultiplayerSaves.cs','tools/MultiplayerRig/RigInject.cs','tools/MultiplayerRig/TaskStatusRigContracts.cs','game/pure-fixes/NativeMemory.cs','game/pure-fixes/PurePatch.cs','game/engine-crash-fixes/TaskStatusPatch.cs')) {
    $sources[$path]=(Get-FileHash (Join-Path $repo $path)).Hash
}
@{schema=1;module='multiplayer-saves-r2-plus-task-status-init-r1';baseline=(Get-FileHash (Join-Path $BaselineBuild 'native/paws_multiplayer_saves.dll')).Hash;sources=$sources;injector=(Get-FileHash (Join-Path $OutputDirectory 'RigInject.exe')).Hash} |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $OutputDirectory 'candidate.json')
