param(
    [Parameter(Mandatory=$true)][string]$Configuration,
    [ValidateSet('SaveCopy','Autosave','LoadCopy','HostLeaves','HostLeavesLaunch')][string]$Scenario,
    [string]$LoadSave,
    [switch]$TestSetupFailure
)
$ErrorActionPreference='Stop'
$config=Get-Content -LiteralPath $Configuration -Raw | ConvertFrom-Json
if (-not $Scenario) {
    Write-Host 'Сетевые тесты Kohan II'
    Write-Host '1 — Сохранение хоста и копия у клиента (около 5 минут)'
    Write-Host '2 — Два автосохранения и копии у клиента (около 7 минут)'
    Write-Host '3 — Загрузка копии сейва (около 10 минут)'
    Write-Host '4 — Выход хоста из лобби (около 5 минут; возможна известная ошибка игры)'
    Write-Host '5 — Уход хоста, запуск и сохранение матча новым хостом (около 6 минут)'
    $choice=Read-Host 'Выберите 1–5. Enter — отмена'
    if (-not $choice) { return }
    $Scenario=@{'1'='SaveCopy';'2'='Autosave';'3'='LoadCopy';'4'='HostLeaves';'5'='HostLeavesLaunch'}[$choice]
    if (-not $Scenario) { throw 'Неизвестный сценарий.' }
}
if ($Scenario -eq 'LoadCopy' -and -not $LoadSave) {
    $LoadSave=Read-Host 'Полный путь к копии сейва клиента (.RSG)'
    if (-not $LoadSave) { return }
}
$run=Join-Path (Join-Path $config.root 'runs') ((Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'-'+$Scenario+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
$script=Join-Path $PSScriptRoot 'Run-Rig.ps1'
$launchArgs=@('-NoProfile','-ExecutionPolicy','Bypass','-File',$script,'-Game',$config.game,'-RigBuild',$config.build,'-RunDirectory',$run,'-Scenario',$Scenario)
if ($LoadSave) {$launchArgs+=@('-LoadSave',$LoadSave)}
if ($TestSetupFailure) {$launchArgs+='-TestSetupFailure'}
Write-Host "Сценарий: $Scenario. Результаты и сейвы: $run"
# A dedicated process gives the ownership job a bounded lifetime even after Ctrl+C.
& (Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe') @launchArgs
$result=$LASTEXITCODE
Write-Host "Готово. Код результата: $result (0 — успешно). Отчёт: $run"
exit $result
