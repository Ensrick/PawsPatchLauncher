param([Parameter(Mandatory=$true)][string]$Python,[Parameter(Mandatory=$true)][string]$LegacyWork,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$out=(Resolve-Path -LiteralPath $OutputDirectory).Path
$repo=Resolve-Path (Join-Path $PSScriptRoot '../..')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll "/out:$out/TaskStatusTests.exe" `
    (Join-Path $repo 'tools/MultiplayerRig/TaskStatusRigContracts.cs') `
    (Join-Path $PSScriptRoot 'TaskStatusPatch.cs') (Join-Path $PSScriptRoot 'TaskStatusTests.cs')
if($LASTEXITCODE -ne 0){throw 'Task status test build failed.'}
& (Join-Path $out 'TaskStatusTests.exe') (Join-Path $LegacyWork 'k2_runtime_1372_20260904.bin') (Join-Path $out 'task-status.bin') |
    Tee-Object -FilePath (Join-Path $out 'transactions.txt')
if($LASTEXITCODE -ne 0){throw 'Task status transaction tests failed.'}
& $Python (Join-Path $PSScriptRoot 'test_task_status.py') --legacy $LegacyWork --candidate (Join-Path $out 'task-status.bin') --out (Join-Path $out 'native-tests.json')
if($LASTEXITCODE -ne 0){throw 'Task status native tests failed.'}
