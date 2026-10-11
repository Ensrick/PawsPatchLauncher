param(
    [Parameter(Mandatory=$true)][string]$Stage,
    [Parameter(Mandatory=$true)][string]$NativeCompiler,
    [Parameter(Mandatory=$true)][string]$GameExecutable,
    [Parameter(Mandatory=$true)][string]$LegacyWorkDirectory
)
$ErrorActionPreference='Stop'
$stagePath=[IO.Path]::GetFullPath($Stage)
New-Item -ItemType Directory -Path $stagePath -Force | Out-Null
& (Join-Path $PSScriptRoot '../game/beta7/build.ps1') `
    -OutputDirectory (Join-Path $stagePath 'beta-helpers') `
    -CityAssistant -LobbyCompatibility -LairRecovery -CameraZoom `
    -CompanyPositionRecovery -ExhaustionRecovery -AiPolicy -BotLobby `
    -FractionalKingdomPoints -GraphicsDiagnostics -SettlementSlots `
    -AllyEconomy -EngineCrashFixes -FoundationCounts -NightmareDifficulty `
    -LargeMaps -PatchVersion '0.4.1' -GameExecutable $GameExecutable `
    -NativeCompiler $NativeCompiler -LegacyWorkDirectory $LegacyWorkDirectory `
    *> (Join-Path $stagePath 'build.log')
if($LASTEXITCODE -ne 0){throw 'Arcane large-map beta build failed; see build.log'}
Write-Output "ARCANE_LARGE_MAP_BETA_READY $stagePath"
