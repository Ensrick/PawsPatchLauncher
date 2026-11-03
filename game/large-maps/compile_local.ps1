param([Parameter(Mandatory=$true)][string]$VerifiedBuild,[Parameter(Mandatory=$true)][string]$RegistryBuild,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$baseline=[IO.Path]::GetFullPath($VerifiedBuild)
$registry=[IO.Path]::GetFullPath($RegistryBuild)
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Choose an unused output directory'}
New-Item -ItemType Directory -Path $out|Out-Null
foreach($f in 'paws_player_colors.ini','paws_patch_versions.ini'){Copy-Item -LiteralPath (Join-Path $baseline $f) -Destination $out}
$game=Join-Path $repo 'game'
$name='k2_paws_lobby_colors_mp_sync_1372.exe'
$exe=Join-Path $out $name
$files=@("$baseline/$name.generated.cs","$registry/ExtendedRegistryPayload.cs","$PSScriptRoot/ExtendedRegistryPatch.cs")
$identity=Join-Path $out 'LargeMapsIdentity.cs'
[IO.File]::WriteAllText($identity,'internal static class LargeMapsLocal { internal const string TemplateHash="339307F847E16C4197986D834526ED946D33062FF13F889EC19B88AF10480D9E"; internal const string PatchVersion="0.4.0"; }')
$files+=$identity
$files+="$game/engine-crash-fixes/TaskStatusPatch.cs"
foreach($f in 'TerrainRuntime','RandomMapPatch','ReleaseStartup','LobbyColorsNative','GamePresentation1372','RandomMapBundle','BuildFeatures','GameText','SyncDiagnostics1372'){$files+="$game/beta7/$f.cs"}
foreach($f in 'PawAssistantRuntime','PawAssistantParties','CityPartyStore','CityDevelopmentText','CityPlanner','CityMilitiaPlanner','CityPolicy','CityResourceOrder','CitySettingsForm'){$files+="$game/city-assistant/$f.cs"}
$files+="$game/fast-transfer/FastTransfer.cs","$game/fast-transfer/FastTransferTests.cs","$game/lobby-compatibility/PawLobbyCompatibility.cs"
$files+="$game/ai-policy/AiPolicyRuntime.cs","$game/ai-policy/PawAiOptions.cs","$game/ai-policy/AiDiagnosticsSnapshot.cs","$baseline/ai-native/AiPolicyPayload.cs","$baseline/nightmare-data/NightmareDifficultyData.cs"
$parts=@(
 @('company-position','CompanyPositionPatch','company-native','CompanyPositionPayload'),
 @('exhaustion-recovery','ExhaustionRecoveryPatch','exhaustion-native','ExhaustionRecoveryPayload'),
 @('bot-lobby','BotLobbyPatch','bot-lobby-native','BotLobbyPayload'),
 @('fractional-points','FractionalPointsPatch','fractional-native','FractionalPointsPayload'),
 @('settlement-slots','SettlementSlotsPatch','slots-native','SettlementSlotsPayload'),
 @('ally-economy','AllyEconomyPatch','ally-native','AllyEconomyPayload'),
 @('foundation-placement','FoundationCountsPatch','foundation-native','FoundationCountsPayload'),
 @('engine-crash-fixes','EngineCrashFixesPatch','crash-native','EngineCrashPayload'),
 @('camera-zoom','CameraZoomPatch','camera-native','CameraZoomPayload'),
 @('lair-recovery','LairRecoveryPatch','lair-native','LairRecoveryPayload'))
foreach($p in $parts){$files+="$game/$($p[0])/$($p[1]).cs","$baseline/$($p[2])/$($p[3]).cs"}
$symbols='RACE_RELATIONS;PROVOKED_NEUTRAL_ATTACK;HERD_RELATIONS_ONLY;PRESERVE_NONINDEPENDENT_OWNER;PAW_COLORS;SYNC_CONTINUE;CITY_ASSISTANT;FAST_SAVE_TRANSFER;LOBBY_COMPATIBILITY;LAIR_RECOVERY_TEST;LAIR_RECOVERY;LOCAL_LARGE_MAPS;COMPANY_POSITION_RECOVERY;EXHAUSTION_RECOVERY;AI_POLICY;BOT_LOBBY;NIGHTMARE_DIFFICULTY;FRACTIONAL_KINGDOM_POINTS;SETTLEMENT_SLOTS;ALLY_ECONOMY;FOUNDATION_COUNTS;ENGINE_CRASH_FIXES;CAMERA_ZOOM_2'
$argsCsc=@('/nologo','/target:winexe','/platform:x86','/optimize+','/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll',"/define:$symbols","/out:$exe")
$resources=@()
foreach($n in 'PawLobbyColorsPayload','PawLobbyColorsFixups','PawCommonUiPayload','PawCommonUiFixups','RandomMapPayload','RandomMapFixups'){$resources+=@{Path="$game/beta7/$n.bin";Name=$n}}
foreach($n in 'AssistantPayload','AssistantFixups'){$resources+=@{Path="$game/city-assistant/$n.bin";Name=$n}}
$resources+=@{Path="$baseline/FastTransferGuards.bin";Name='FastTransferGuards'},@{Path="$baseline/paws_lobby_compatibility.dll";Name='PawLobbyCompatibilityNative'}
foreach($r in $resources){$r.Path=[IO.Path]::GetFullPath($r.Path);$argsCsc+='/resource:'+$r.Path+','+$r.Name}
$files=@($files|ForEach-Object {[IO.Path]::GetFullPath($_)})
foreach($f in $files){if(!(Test-Path -LiteralPath $f)){throw "Missing compilation input $f"}}
$argsCsc+=$files
$inputs=@($files)+@($resources|ForEach-Object {$_.Path})
$inputs|ForEach-Object {Get-FileHash -LiteralPath $_ -Algorithm SHA256}|Select-Object Path,Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'inputs.json')
& "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" @argsCsc
if($LASTEXITCODE -ne 0){throw 'Local registry compilation failed'}
foreach($mode in '--quiet-startup-self-test','--common-ui-self-test','--lobby-payload-test','--assistant-self-test','--fast-transfer-self-test'){
 $p=Start-Process -FilePath $exe -ArgumentList $mode -WindowStyle Hidden -PassThru -Wait
 if($p.ExitCode -ne 0){throw "Self-test failed $mode"}
}
Write-Output "LOCAL_EXTENDED_REGISTRY_HELPER_READY $exe"
