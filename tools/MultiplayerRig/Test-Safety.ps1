# Tests launcher process ownership and path refusals with disposable sleeping
# programs. Does not start or modify Kohan II; can run while a rig owns its copies.
param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) {throw 'Use a fresh test directory.'}
New-Item -ItemType Directory -Path $out | Out-Null
. (Join-Path $PSScriptRoot 'RigSafety.ps1')
. (Join-Path $PSScriptRoot 'RigLauncher.ps1')
$checks=0
function Require($value,$label) {if (-not $value) {throw $label}; $script:checks++}
function Reject($operation,$label) {$rejected=$false;try {& $operation}catch {$rejected=$true};Require $rejected $label}
Reject {Assert-RigFixture $out} 'Unmarked game accepted'
$fakeGame=Join-Path $out 'game';New-Item -ItemType Directory -Path $fakeGame | Out-Null
Reject {Assert-FreshRigRun $fakeGame (Join-Path $out 'outside')} 'Outside run accepted'
$run=Join-Path $out 'runs\one';Assert-FreshRigRun $fakeGame $run
New-Item -ItemType Directory -Path $run | Out-Null
$sentinel=Join-Path $run 'keep.txt';Set-Content -LiteralPath $sentinel 'evidence'
Reject {Assert-FreshRigRun $fakeGame $run} 'Existing run accepted'
Require ((Get-Content -LiteralPath $sentinel) -eq 'evidence') 'Existing evidence altered'
$lockPath=Join-Path $out 'lock'
$lock=[IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None')
try {Reject {$other=[IO.File]::Open($lockPath,'OpenOrCreate','ReadWrite','None');$other.Dispose()} 'Exclusive lock failed'} finally {$lock.Dispose()}
$source=Join-Path $out 'Sleeper.cs';$exe=Join-Path $out 'Sleeper.exe'
Set-Content -LiteralPath $source 'class Sleeper { static void Main() { System.Threading.Thread.Sleep(60000); } }'
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe') /nologo /target:exe "/out:$exe" $source
if ($LASTEXITCODE -ne 0) {throw 'Sleeper compilation failed'}
try {
    $one=[PawsRig.Launcher]::Start($exe,$out,'','')
    $two=[PawsRig.Launcher]::Start($exe,$out,'','')
    Require ((@(Get-RigCopies).Count -eq 2) -and (@(Get-RigCopies) -contains $one) -and (@(Get-RigCopies) -contains $two)) 'Owned handles not tracked'
} finally {Stop-RigCopies}
Require (@(Get-RigCopies).Count -eq 0) 'Ownership cleanup failed'
Require (-not (Get-Process -Id $one,$two -ErrorAction SilentlyContinue)) 'Test child survived cleanup'
# Crash the owner process; its kill-on-close job must stop its own child.
$ownerScript=Join-Path $out 'Owner.ps1';$pidFile=Join-Path $out 'child.txt'
$launcher=Join-Path $PSScriptRoot 'RigLauncher.ps1'
$ownerText=". '"+$launcher.Replace("'","''")+"'`n"+'$child=[PawsRig.Launcher]::Start('+"'"+$exe.Replace("'","''")+"','"+$out.Replace("'","''")+"','','')`n"+'[IO.File]::WriteAllText('+"'"+$pidFile.Replace("'","''")+"', [string]`$child)`nStart-Sleep 60"
Set-Content -LiteralPath $ownerScript $ownerText
$owner=Start-Process -FilePath (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$ownerScript+'"')) -WindowStyle Hidden -PassThru
try {
    $deadline=(Get-Date).AddSeconds(20)
    while (-not (Test-Path -LiteralPath $pidFile) -and (Get-Date) -lt $deadline) {Start-Sleep -Milliseconds 100}
    Require (Test-Path -LiteralPath $pidFile) 'Owner did not start child'
    $childId=[int](Get-Content -LiteralPath $pidFile)
    $childProcess=Get-Process -Id $childId
    $owner.Kill();$owner.WaitForExit()
    Require ($childProcess.WaitForExit(5000)) 'Child survived owner termination'
} finally {if (-not $owner.HasExited) {$owner.Kill();$owner.WaitForExit()};$owner.Dispose()}
@{passed=$true;checks=$checks;gameStarted=$false} | ConvertTo-Json | Set-Content (Join-Path $out 'result.json')
Write-Host "RIG_SAFETY_PASS $checks"
