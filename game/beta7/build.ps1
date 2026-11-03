param([Parameter(Mandatory=$true)][string]$OutputDirectory, [switch]$CityAssistant, [string]$LegacyWorkDirectory, [switch]$LobbyCompatibility, [string]$NativeCompiler, [switch]$LairWoundedTest, [switch]$LairRecovery, [switch]$CameraZoom, [switch]$CompanyPositionRecovery, [switch]$ExhaustionRecovery, [switch]$AiPolicy, [switch]$BotLobby, [switch]$FractionalKingdomPoints, [switch]$GraphicsDiagnostics, [switch]$SettlementSlots, [switch]$AllyEconomy, [switch]$EngineCrashFixes, [switch]$FoundationCounts, [switch]$NightmareDifficulty, [string]$PatchVersion, [string]$LocalLargeMapsSource, [switch]$LargeMaps, [string]$GameExecutable, [switch]$UniformUi, [switch]$TerrainTexturePool, [switch]$SelectionScroll, [switch]$GroupAttackRecovery, [switch]$CompanyBehavior)
$ErrorActionPreference = 'Stop'
if($LairWoundedTest){$LairRecovery=$true}
$out = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $out) { throw 'Choose an unused output directory.' }
New-Item -ItemType Directory -Path $out | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$variants = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'variants.json') -Raw | ConvertFrom-Json
$shared = 'TerrainRuntime.cs','RandomMapPatch.cs','ReleaseStartup.cs','LobbyColorsNative.cs','GamePresentation1372.cs','RandomMapBundle.cs','BuildFeatures.cs','GameText.cs','SyncDiagnostics1372.cs'
$resources = 'PawLobbyColorsPayload','PawLobbyColorsFixups','PawCommonUiPayload','PawCommonUiFixups','RandomMapPayload','RandomMapFixups'
$assistant = Join-Path $PSScriptRoot '../city-assistant'
$transfer = Join-Path $PSScriptRoot '../fast-transfer'
$work = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$python = 'C:\Users\Paw\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe'
if($NightmareDifficulty){
    if(!$AiPolicy){throw 'Nightmare difficulty belongs to the selectable AI improvements module.'}
    $nightmareData=Join-Path $out 'nightmare-data'
    & $python (Join-Path $PSScriptRoot '../nightmare-difficulty/prepare.py') --out $nightmareData --language ru
    if($LASTEXITCODE -ne 0){throw 'Nightmare difficulty preparation failed'}
    $nightmareFixture=Join-Path $out 'nightmare-data-test'
    & $python (Join-Path $PSScriptRoot '../nightmare-difficulty/prepare.py') --out $nightmareFixture --language ru
    if($LASTEXITCODE -ne 0){throw 'Nightmare difficulty fixture failed'}
    $nightmareTests=Join-Path $out 'NightmareDataTests.exe'
    & $compiler /nologo /target:exe /platform:x86 "/out:$nightmareTests" (Join-Path $PSScriptRoot '../nightmare-difficulty/DataTests.cs') (Join-Path $nightmareFixture 'NightmareDifficultyData.cs')
    if($LASTEXITCODE -ne 0){throw 'Nightmare difficulty tests compilation failed'}
    & $nightmareTests $nightmareFixture
    if($LASTEXITCODE -ne 0){throw 'Nightmare difficulty data tests failed'}
}
if($LobbyCompatibility) {
    if(!$CityAssistant -or !$NativeCompiler){throw 'Lobby compatibility is part of the full beta helper build; specify -CityAssistant and -NativeCompiler.'}
    $lobby=Join-Path $PSScriptRoot '../lobby-compatibility'
    $packageVersion=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'paws_patch_versions.ini')),'(?m)^PawPatch=([^\r\n]+)').Groups[1].Value
    $helperVersion=[regex]::Match([IO.File]::ReadAllText((Join-Path $lobby 'PawLobbyCompatibility.cs')),'internal const string Version = "([^"]+)";').Groups[1].Value
    if(!$packageVersion -or $packageVersion -ne $helperVersion){throw 'Lobby compatibility version differs from package version.'}
    $lobbySource=Join-Path $lobby 'PawLobbyCompatibility.cs'
    if($PatchVersion){
        if($PatchVersion -notmatch '^0\.\d+\.\d+(-beta\.\d+)?$'){throw 'Invalid patch identity override'}
        $lobbySource=Join-Path $out 'PawLobbyCompatibility.cs'
        [IO.File]::WriteAllText($lobbySource, [IO.File]::ReadAllText((Join-Path $lobby 'PawLobbyCompatibility.cs')).Replace('"'+$helperVersion+'"','"'+$PatchVersion+'"'), [Text.UTF8Encoding]::new($false))
    }

    & $NativeCompiler -shared -Wall -Werror (Join-Path $lobby 'lobby_compatibility.c') -o (Join-Path $out 'paws_lobby_compatibility.dll') -lkernel32 -luser32
    if($LASTEXITCODE -ne 0){throw 'Lobby compatibility DLL build failed.'}
    & $NativeCompiler -Wall -Werror -DPAW_TEST (Join-Path $lobby 'lobby_compatibility.c') -o (Join-Path $out 'LobbyProtocolTests.exe') -lkernel32 -luser32
    if($LASTEXITCODE -ne 0){throw 'Lobby protocol test build failed.'}
    & (Join-Path $out 'LobbyProtocolTests.exe')
    if($LASTEXITCODE -ne 0){throw 'Lobby protocol test failed.'}
}
if ($CityAssistant) {
    if ($LegacyWorkDirectory) {
        $work = [IO.Path]::GetFullPath($LegacyWorkDirectory)
    } elseif (-not (Test-Path -LiteralPath (Join-Path $work 'city_assistant_1372'))) {
        # Accepted local audit source after the workspace migration. Rebuilders
        # elsewhere should supply their verified source root explicitly.
        $work = 'G:\CodexData\projects\Codex\2026-08-11\kohan-ii-d-steamlibrary-steamapps-common\work'
    }
    foreach ($required in 'city_assistant_1372','city_policy_v2','fast_transfer_1372') {
        if (-not (Test-Path -LiteralPath (Join-Path $work $required))) { throw "Missing accepted legacy source $required. Set -LegacyWorkDirectory to its work directory." }
    }
    if($LobbyCompatibility){
        $browserTest=Join-Path $out 'LobbyBrowserTest.dll'
        & $NativeCompiler -shared -DPAW_TEST -Wall -Werror (Join-Path $lobby 'lobby_compatibility.c') -o $browserTest -lkernel32 -luser32
        if($LASTEXITCODE -ne 0){throw 'Browser label test build failed'}
        & $python (Join-Path $lobby 'test_browser_label.py') --legacy $work --dll $browserTest
        if($LASTEXITCODE -ne 0){throw 'Browser label regression failed'}
    }
    Write-Output "Verified legacy sources: $work"
    & $python (Join-Path $assistant 'build_policy_native.py') --legacy (Join-Path $work 'city_assistant_1372') --out (Join-Path $out 'native')
    if ($LASTEXITCODE -ne 0) { throw 'Native city policy generation failed.' }
    foreach ($resource in 'AssistantPayload','AssistantFixups') {
        if ((Get-FileHash (Join-Path $out "native/$resource.bin")).Hash -ne (Get-FileHash (Join-Path $assistant "$resource.bin")).Hash) { throw "Checked-in native resource differs: $resource" }
    }
    & $python (Join-Path $transfer 'prepare_guards.py') --work $work --out $out
    if ($LASTEXITCODE -ne 0) { throw 'Transfer guards generation failed.' }
}
function Replace-StartupAnchor([string]$text, [string]$before, [string]$after) {
    if (($text.Split([string[]]@($before),[StringSplitOptions]::None).Length-1) -ne 1) { throw "Startup source anchor changed: $before" }
    return $text.Replace($before,$after)
}
if ($CityAssistant) {
    $syncTests = Join-Path $out 'SyncDiagnosticsTests.exe'
    $syncFixtures = Join-Path $out 'sync-diagnostics'
    & $compiler /nologo /target:exe /platform:x86 "/out:$syncTests" (Join-Path $PSScriptRoot 'SyncDiagnostics1372.cs') (Join-Path $PSScriptRoot 'SyncDiagnosticsTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Sync diagnostics test compilation failed.' }
    & $syncTests $syncFixtures (Join-Path $work 'k2_runtime_1372_20260904.bin')
    if ($LASTEXITCODE -ne 0) { throw 'Sync diagnostics guard/fixture failed.' }
    & $python (Join-Path $PSScriptRoot 'test_sync_diagnostics.py') --fixtures $syncFixtures --deps (Join-Path $work 'lobby_colors_1372/deps_r15')
    if ($LASTEXITCODE -ne 0) { throw 'Sync diagnostics native regression failed.' }
}
if($LairRecovery) {
    if(!$CityAssistant -or !$LobbyCompatibility){throw 'Lair recovery requires the full beta helper and lobby compatibility.'}
    $lair=Join-Path $PSScriptRoot '../lair-recovery'
    $lairNative=Join-Path $out 'lair-native'
    & $python (Join-Path $lair 'build_native.py') --legacy $work --out $lairNative
    if($LASTEXITCODE -ne 0){throw 'Lair native build failed.'}
    & $python (Join-Path $lair 'test_native.py') --legacy $work --native $lairNative
    if($LASTEXITCODE -ne 0){throw 'Lair native regression failed.'}
    $lairTests=Join-Path $out 'LairRecoveryTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:LairRecoveryTests "/out:$lairTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $lair 'LairRecoveryPatch.cs') (Join-Path $lairNative 'LairRecoveryPayload.cs') (Join-Path $lair 'LairRecoveryTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Lair transaction tests compilation failed.'}
    & $lairTests
    if($LASTEXITCODE -ne 0){throw 'Lair transaction regression failed.'}
}
if($CameraZoom) {
    if(!$CityAssistant -or !$LobbyCompatibility -or !$LairRecovery){throw 'Camera zoom requires the complete Arcane beta helper.'}
    $camera=Join-Path $PSScriptRoot '../camera-zoom'
    $cameraNative=Join-Path $out 'camera-native'
    & $python (Join-Path $camera 'build_native.py') --legacy $work --out $cameraNative
    if($LASTEXITCODE -ne 0){throw 'Camera native build failed.'}
    $cameraTests=Join-Path $out 'CameraZoomTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:CameraZoomTests "/out:$cameraTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $camera 'CameraZoomPatch.cs') (Join-Path $cameraNative 'CameraZoomPayload.cs') (Join-Path $camera 'CameraZoomTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Camera transaction tests compilation failed.'}
    & $cameraTests $cameraNative
    if($LASTEXITCODE -ne 0){throw 'Camera transaction regression failed.'}
    & $python (Join-Path $camera 'test_native.py') --legacy $work --native $cameraNative
    if($LASTEXITCODE -ne 0){throw 'Camera native regression failed.'}
}
if($CompanyPositionRecovery) {
    if(!$CityAssistant -or !$LobbyCompatibility -or !$CameraZoom -or !$LairRecovery){throw 'Company recovery requires the complete current Arcane helper.'}
    $company=Join-Path $PSScriptRoot '../company-position'
    $companyNative=Join-Path $out 'company-native'
    & $python (Join-Path $company 'build_native.py') --legacy $work --out $companyNative
    if($LASTEXITCODE -ne 0){throw 'Company native build failed.'}
    $companyTests=Join-Path $out 'CompanyPositionTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:CompanyPositionTests "/out:$companyTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $company 'CompanyPositionPatch.cs') (Join-Path $companyNative 'CompanyPositionPayload.cs') (Join-Path $company 'CompanyPositionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Company transaction tests compilation failed.'}
    & $companyTests $companyNative
    if($LASTEXITCODE -ne 0){throw 'Company transaction regression failed.'}
    & $python (Join-Path $company 'test_native.py') --legacy $work --native $companyNative
    if($LASTEXITCODE -ne 0){throw 'Company native regression failed.'}
}
if($ExhaustionRecovery) {
    if(!$CityAssistant -or !$LobbyCompatibility -or !$CameraZoom -or !$LairRecovery -or !$CompanyPositionRecovery){throw 'Exhaustion recovery requires the complete current Arcane helper.'}
    $exhaustion=Join-Path $PSScriptRoot '../exhaustion-recovery'
    $exhaustionNative=Join-Path $out 'exhaustion-native'
    & $python (Join-Path $exhaustion 'build_native.py') --legacy $work --out $exhaustionNative
    if($LASTEXITCODE -ne 0){throw 'Exhaustion native build failed.'}
    $exhaustionTests=Join-Path $out 'ExhaustionRecoveryTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:ExhaustionRecoveryTests "/out:$exhaustionTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $exhaustion 'ExhaustionRecoveryPatch.cs') (Join-Path $exhaustionNative 'ExhaustionRecoveryPayload.cs') (Join-Path $exhaustion 'ExhaustionRecoveryTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Exhaustion transaction tests compilation failed.'}
    & $exhaustionTests $exhaustionNative
    if($LASTEXITCODE -ne 0){throw 'Exhaustion transaction regression failed.'}
    & $python (Join-Path $exhaustion 'test_native.py') --legacy $work --native $exhaustionNative
    if($LASTEXITCODE -ne 0){throw 'Exhaustion native regression failed.'}
}
if($AiPolicy) {
    if(!$ExhaustionRecovery -or !$NativeCompiler){throw 'AI local test requires exhaustion recovery and native compiler.'}
    $ai=Join-Path $PSScriptRoot '../ai-policy'
    $aiNative=Join-Path $out 'ai-native'
    & $python (Join-Path $ai 'build_native.py') --legacy $work --out $aiNative --compiler $NativeCompiler
    if($LASTEXITCODE -ne 0){throw 'AI policy native build failed.'}
    & $python (Join-Path $ai 'test_native.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI native wrapper tests failed'}
    & $python (Join-Path $ai 'test_limits.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI native regression failed.'}
    & $python (Join-Path $ai 'test_routing.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI routing regression failed.'}
    & $python (Join-Path $ai 'test_defense.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI peaceful defense regression failed.'}
    & $python (Join-Path $ai 'test_scouting.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI pending exploration regression failed.'}
    & $python (Join-Path $ai 'test_clearing.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI clearing regression failed.'}
    & $python (Join-Path $ai 'test_clearing_priority.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI clearing priority regression failed.'}
    & $python (Join-Path $ai 'test_region_clearing.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI regional defense and target selection regression failed.'}
    & $python (Join-Path $ai 'test_opening_capture.py') --legacy $work --native $aiNative
    if($LASTEXITCODE -ne 0){throw 'AI opening capture regression failed.'}
    foreach($test in 'test_builder_clearing.py','test_clearing_rally.py','test_region_ratio.py','test_expansion_fallback.py','test_economy.py','test_clearing_readiness.py','test_militia.py'){
        & $python (Join-Path $ai $test) --legacy $work --native $aiNative
        if($LASTEXITCODE -ne 0){throw 'AI construction/staging regression failed.'}
    }
    foreach($test in 'test_opening_lairs.py','test_clearing_route.py','test_expansion_pulse.py','test_fiber_sync.py','test_strategic_queue.py','test_supply_notice.py','test_recruit_counts.py','test_builder_fleet.py','test_builder_claims.py','test_army_upgrade.py','test_upgrade_completion.py','test_worker_survival.py','test_city_sharing.py','test_peer_parity.py'){
        & $python (Join-Path $ai $test) --legacy $work --native $aiNative
        if($LASTEXITCODE -ne 0){throw "AI release regression failed: $test"}
    }
    $aiOptionsTests=Join-Path $out 'AiOptionsTests.exe'
    & $compiler /nologo /target:exe /r:System.Web.Extensions.dll "/out:$aiOptionsTests" (Join-Path $ai 'PawAiOptions.cs') (Join-Path $ai 'OptionsTests.cs')
    if($LASTEXITCODE -ne 0){throw 'AI option tests compilation failed.'}
    & $aiOptionsTests
    if($LASTEXITCODE -ne 0){throw 'AI option tests failed.'}
    $aiSnapshotTests=Join-Path $out 'AiSnapshotTests.exe'
    & $compiler /nologo /target:exe /r:System.Web.Extensions.dll "/out:$aiSnapshotTests" (Join-Path $ai 'AiDiagnosticsSnapshot.cs') (Join-Path $ai 'SnapshotTests.cs')
    if($LASTEXITCODE -ne 0){throw 'AI snapshot test compilation failed.'}
    & $aiSnapshotTests
    if($LASTEXITCODE -ne 0){throw 'AI snapshot regression failed.'}
    $aiRuntimeTests=Join-Path $out 'AiRuntimeTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll /main:AiRuntimeTests "/out:$aiRuntimeTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $ai 'AiPolicyRuntime.cs') (Join-Path $ai 'AiDiagnosticsSnapshot.cs') (Join-Path $aiNative 'AiPolicyPayload.cs') (Join-Path $ai 'AiRuntimeTests.cs')
    if($LASTEXITCODE -ne 0){throw 'AI runtime test compilation failed.'}
    & $aiRuntimeTests
    if($LASTEXITCODE -ne 0){throw 'AI runtime regression failed.'}
}
if($BotLobby){
    if(!$AiPolicy){throw 'Bulk bot lobby is part of the local AI experiment.'}
    $botLobbySource=Join-Path $PSScriptRoot '../bot-lobby'
    $botLobbyNative=Join-Path $out 'bot-lobby-native'
    & $python (Join-Path $botLobbySource 'build_native.py') --legacy $work --out $botLobbyNative --compiler $NativeCompiler
    if($LASTEXITCODE -ne 0){throw 'Bot lobby native build failed.'}
    & $python (Join-Path $botLobbySource 'test_native.py') --legacy $work --native $botLobbyNative
    if($LASTEXITCODE -ne 0){throw 'Bot lobby native regression failed.'}
    $botLobbyTests=Join-Path $out 'BotLobbyTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:BotLobbyTests "/out:$botLobbyTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $botLobbySource 'BotLobbyPatch.cs') (Join-Path $botLobbyNative 'BotLobbyPayload.cs') (Join-Path $botLobbySource 'BotLobbyTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Bot lobby transaction compilation failed.'}
    & $botLobbyTests
    if($LASTEXITCODE -ne 0){throw 'Bot lobby transaction regression failed.'}
}
if($FractionalKingdomPoints){
    $fractions=Join-Path $PSScriptRoot '../fractional-points'
    $fractionsNative=Join-Path $out 'fractional-native'
    & $python (Join-Path $fractions 'build_native.py') --legacy $work --out $fractionsNative
    if($LASTEXITCODE -ne 0){throw 'Fractional points build failed'}
    & $python (Join-Path $fractions 'test_native.py') --legacy $work --native $fractionsNative
    if($LASTEXITCODE -ne 0){throw 'Fractional points native tests failed'}
    $fractionTests=Join-Path $out 'FractionalPointsTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:FractionalPointsTests "/out:$fractionTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $fractions 'FractionalPointsPatch.cs') (Join-Path $fractionsNative 'FractionalPointsPayload.cs') (Join-Path $fractions 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Fractional points transaction compilation failed'}
    & $fractionTests
    if($LASTEXITCODE -ne 0){throw 'Fractional points transactions failed'}
}
if($SettlementSlots){
    $slots=Join-Path $PSScriptRoot '../settlement-slots'
    $slotsNative=Join-Path $out 'slots-native'
    & $python (Join-Path $slots 'build_native.py') --legacy $work --out $slotsNative
    if($LASTEXITCODE -ne 0){throw 'Settlement layout build failed'}
    & $python (Join-Path $slots 'test_native.py') --legacy $work --native $slotsNative
    if($LASTEXITCODE -ne 0){throw 'Settlement slots native layout checks failed'}
    $slotTests=Join-Path $out 'SettlementSlotsTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:SettlementSlotsTests "/out:$slotTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $slots 'SettlementSlotsPatch.cs') (Join-Path $slotsNative 'SettlementSlotsPayload.cs') (Join-Path $slots 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Settlement slots test compilation failed'}
    & $slotTests
    if($LASTEXITCODE -ne 0){throw 'Settlement slots transaction checks failed'}
}
if($CompanyBehavior){
    if(!$ExhaustionRecovery){throw 'Company behavior requires ExhaustionRecovery for the optional enabled mode.'}
    $companyBehavior=Join-Path $PSScriptRoot '../company-behavior'
    & $python (Join-Path $companyBehavior 'test.py') --legacy $work --out (Join-Path $out 'company-behavior-tests')
    if($LASTEXITCODE -ne 0){throw 'Company behavior tests failed.'}
}
if($GroupAttackRecovery){
    $groupRecovery=Join-Path $PSScriptRoot '../group-attack-recovery'
    $groupTests=Join-Path $out 'GroupAttackRecoveryTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:GroupAttackRecoveryTests "/out:$groupTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $groupRecovery 'GroupAttackRecoveryPatch.cs') (Join-Path $groupRecovery 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Group attack recovery test compilation failed'}
    & $groupTests
    if($LASTEXITCODE -ne 0){throw 'Group attack recovery transaction checks failed'}
    & $python (Join-Path $groupRecovery 'test_native.py') --legacy $work --emitter $groupTests
    if($LASTEXITCODE -ne 0){throw 'Group attack recovery native checks failed'}
    $groupSpeedNative=Join-Path $out 'group-speed-native'
    & $python (Join-Path $groupRecovery 'build_speed.py') --legacy $work --out $groupSpeedNative
    if($LASTEXITCODE -ne 0){throw 'Group movement speed build failed'}
    $speedTests=Join-Path $out 'GroupMovementSpeedTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:GroupMovementSpeedTests "/out:$speedTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $groupRecovery 'GroupMovementSpeedPatch.cs') (Join-Path $groupSpeedNative 'GroupMovementSpeedPayload.cs') (Join-Path $groupRecovery 'SpeedTransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Group movement speed test compilation failed'}
    & $speedTests
    if($LASTEXITCODE -ne 0){throw 'Group movement speed transaction checks failed'}
    & $python (Join-Path $groupRecovery 'test_speed.py') --legacy $work --emitter $speedTests
    if($LASTEXITCODE -ne 0){throw 'Group movement speed native checks failed'}
}
if($SelectionScroll){
    if(!$NativeCompiler){throw 'Selection scrolling requires the native compiler.'}
    $selection=Join-Path $PSScriptRoot '../selection-scroll'
    $selectionNative=Join-Path $out 'selection-native'
    & $python (Join-Path $selection 'build_native.py') --legacy $work --out $selectionNative --compiler $NativeCompiler
    if($LASTEXITCODE -ne 0){throw 'Selection scrolling native build failed'}
    & $python (Join-Path $selection 'test_native.py') --legacy $work --native $selectionNative
    if($LASTEXITCODE -ne 0){throw 'Selection scrolling native tests failed'}
    $selectionTests=Join-Path $out 'SelectionScrollTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:SelectionScrollTests "/out:$selectionTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $selection 'SelectionScrollPatch.cs') (Join-Path $selectionNative 'SelectionScrollPayload.cs') (Join-Path $selection 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Selection scrolling transaction compilation failed'}
    & $selectionTests
    if($LASTEXITCODE -ne 0){throw 'Selection scrolling transaction checks failed'}
    & $python (Join-Path $selection 'build_formation.py') --legacy $work --out $selectionNative
    if($LASTEXITCODE -ne 0){throw 'Group formation build failed'}
    & $python (Join-Path $selection 'test_formation.py') --legacy $work --native $selectionNative
    if($LASTEXITCODE -ne 0){throw 'Group formation native tests failed'}
    $formationTests=Join-Path $out 'GroupFormationTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:GroupFormationTests "/out:$formationTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $selection 'GroupFormationPatch.cs') (Join-Path $selectionNative 'GroupFormationPayload.cs') (Join-Path $selection 'FormationTransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Group formation transaction compilation failed'}
    & $formationTests
    if($LASTEXITCODE -ne 0){throw 'Group formation transaction checks failed'}
    & $python (Join-Path $selection 'build_radius.py') --legacy $work --out $selectionNative --compiler $NativeCompiler
    if($LASTEXITCODE -ne 0){throw 'Group formation radius build failed'}
    & $python (Join-Path $selection 'test_radius.py') --legacy $work --native $selectionNative
    if($LASTEXITCODE -ne 0){throw 'Group formation radius native tests failed'}
    $radiusTests=Join-Path $out 'GroupFormationRadiusTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:GroupFormationRadiusTests "/out:$radiusTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $selection 'GroupFormationRadiusPatch.cs') (Join-Path $selectionNative 'GroupFormationRadiusPayload.cs') (Join-Path $selection 'RadiusTransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Group formation radius transaction compilation failed'}
    & $radiusTests
    if($LASTEXITCODE -ne 0){throw 'Group formation radius transaction checks failed'}
}
if($AllyEconomy){
    if(!$NativeCompiler){throw 'Ally economy requires the native compiler.'}
    $ally=Join-Path $PSScriptRoot '../ally-economy'
    $allyNative=Join-Path $out 'ally-native'
    & $python (Join-Path $ally 'build_native.py') --legacy $work --out $allyNative --compiler $NativeCompiler
    if($LASTEXITCODE -ne 0){throw 'Ally economy native build failed'}
    & $python (Join-Path $ally 'test_native.py') --legacy $work --native $allyNative
    if($LASTEXITCODE -ne 0){throw 'Ally economy native checks failed'}
    $allyTests=Join-Path $out 'AllyEconomyTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:AllyEconomyTests "/out:$allyTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $ally 'AllyEconomyPatch.cs') (Join-Path $allyNative 'AllyEconomyPayload.cs') (Join-Path $ally 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Ally economy transaction compilation failed'}
    & $allyTests
    if($LASTEXITCODE -ne 0){throw 'Ally economy transactions failed'}
}
if($FoundationCounts){
    if(!$CityAssistant -or !$NativeCompiler){throw 'Foundation distribution requires the Arcane helper and native compiler.'}
    $foundation=Join-Path $PSScriptRoot '../foundation-placement'
    $foundationNative=Join-Path $out 'foundation-native'
    & $python (Join-Path $foundation 'build_native.py') --legacy $work --out $foundationNative --compiler $NativeCompiler
    if($LASTEXITCODE -ne 0){throw 'Foundation distribution build failed'}
    & $python (Join-Path $foundation 'test_counts.py') --legacy $work --native $foundationNative
    if($LASTEXITCODE -ne 0){throw 'Foundation distribution native tests failed'}
    & $python (Join-Path $foundation 'test_final.py') --legacy $work --native $foundationNative
    if($LASTEXITCODE -ne 0){throw 'Final placed-pool distribution tests failed'}
    $foundationTests=Join-Path $out 'FoundationCountsTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:FoundationCountsTests "/out:$foundationTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $foundation 'FoundationCountsPatch.cs') (Join-Path $foundationNative 'FoundationCountsPayload.cs') (Join-Path $foundation 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Foundation distribution transaction compilation failed'}
    & $foundationTests
    if($LASTEXITCODE -ne 0){throw 'Foundation distribution transaction checks failed'}
    $foundationMemoryTests=Join-Path $out 'FoundationNativeMemoryTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:FoundationNativeMemoryTests "/out:$foundationMemoryTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $foundation 'FoundationCountsPatch.cs') (Join-Path $foundationNative 'FoundationCountsPayload.cs') (Join-Path $foundation 'NativeMemoryTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Foundation native memory test compilation failed'}
    $plannerOffset=(Get-Content (Join-Path $foundationNative 'counts.json') -Raw|ConvertFrom-Json).exports.plan_map
    & $foundationMemoryTests $plannerOffset
    if($LASTEXITCODE -ne 0){throw 'Foundation native memory checks failed'}
}
if($EngineCrashFixes){
    if(!$CityAssistant){throw 'Engine crash fixes require the complete monitored helper.'}
    $crashFixes=Join-Path $PSScriptRoot '../engine-crash-fixes'
    $crashNative=Join-Path $out 'crash-native'
    & $python (Join-Path $crashFixes 'build_native.py') --legacy $work --out $crashNative
    if($LASTEXITCODE -ne 0){throw 'Engine crash fix build failed'}
    & $python (Join-Path $crashFixes 'test_native.py') --legacy $work --native $crashNative
    if($LASTEXITCODE -ne 0){throw 'Engine crash fix regressions failed'}
    $crashTests=Join-Path $out 'EngineCrashTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /main:EngineCrashTests "/out:$crashTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $PSScriptRoot 'ReleaseStartup.cs') (Join-Path $PSScriptRoot 'RandomMapPatch.cs') (Join-Path $PSScriptRoot 'RandomMapBundle.cs') (Join-Path $crashFixes 'EngineCrashFixesPatch.cs') (Join-Path $crashNative 'EngineCrashPayload.cs') (Join-Path $crashFixes 'TransactionTests.cs')
    if($LASTEXITCODE -ne 0){throw 'Engine crash transaction compilation failed'}
    & $crashTests
    if($LASTEXITCODE -ne 0){throw 'Engine crash transaction checks failed'}
    $probeTests=Join-Path $out 'ProbeWindowsTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /optimize+ "/out:$probeTests" (Join-Path $crashFixes 'ProbeWindowsTests.cs') (Join-Path $crashNative 'EngineCrashPayload.cs')
    if($LASTEXITCODE -ne 0){throw 'Windows probe test compilation failed'}
    & $probeTests
    if($LASTEXITCODE -ne 0){throw 'Windows probe checks failed'}
}
if($GraphicsDiagnostics){
    if(!$NativeCompiler -or !$CityAssistant){throw 'Embedded graphics recorder requires native compiler and complete helper'}
    $graphics=Join-Path $PSScriptRoot '../graphics-diagnostics'
    $graphicsExe=Join-Path $out 'PawsGraphicsRecorder.exe'
    & $NativeCompiler -mwindows -Wall -Werror (Join-Path $graphics 'recorder.c') (Join-Path $graphics 'debug-api.def') -o $graphicsExe -lkernel32 -lshell32
    if($LASTEXITCODE -ne 0){throw 'Graphics recorder build failed'}
}
if($LargeMaps){
    if(!$CityAssistant -or !$LobbyCompatibility -or !$NativeCompiler -or !$PatchVersion -or !$GameExecutable -or $LocalLargeMapsSource){throw 'Large maps require a full versioned Arcane build, native compiler and verified game executable.'}
    $largeMapsDirectory=Join-Path $PSScriptRoot '../large-maps'
    $registryNative=Join-Path $out 'registry-native'
    & $python (Join-Path $largeMapsDirectory 'build_registry.py') --legacy $work --out $registryNative --compiler $NativeCompiler --exe $GameExecutable
    if($LASTEXITCODE -ne 0){throw 'Large-map native build failed'}
    & $python (Join-Path $largeMapsDirectory 'test_registry.py') --legacy $work --native $registryNative --exe $GameExecutable
    if($LASTEXITCODE -ne 0){throw 'Large-map native regressions failed'}
    $registryTests=Join-Path $out 'RegistryInstallerTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$registryTests" (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $largeMapsDirectory 'ExtendedRegistryPatch.cs') (Join-Path $largeMapsDirectory 'InstallerTests.cs') (Join-Path $registryNative 'ExtendedRegistryPayload.cs')
    if($LASTEXITCODE -ne 0){throw 'Registry installer test compilation failed'}
    & $registryTests
    if($LASTEXITCODE -ne 0){throw 'Registry installer regressions failed'}
    $largeMapsIdentity=Join-Path $out 'LargeMapsIdentity.cs'
    [IO.File]::WriteAllText($largeMapsIdentity,'internal static class LargeMapsLocal { internal const string TemplateHash="339307F847E16C4197986D834526ED946D33062FF13F889EC19B88AF10480D9E"; internal const string PatchVersion="'+$PatchVersion+'"; }')
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'paws_player_colors.ini') -Destination $out
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'paws_patch_versions.ini') -Destination $out
if($PatchVersion){
    $versionFile=Join-Path $out 'paws_patch_versions.ini'
    [IO.File]::WriteAllText($versionFile, [regex]::Replace([IO.File]::ReadAllText($versionFile),'(?m)^PawPatch=[^\r\n]+','PawPatch='+$PatchVersion), [Text.UTF8Encoding]::new($false))
}

foreach ($variant in $variants.PSObject.Properties) {
    $exe = Join-Path $out $variant.Name
    $arguments = @('/nologo','/target:winexe','/platform:x86','/optimize+',
        '/r:System.Core.dll','/r:System.Windows.Forms.dll','/r:System.Drawing.dll',
        ("/define:" + $variant.Value + $(if($CityAssistant){';CITY_ASSISTANT;FAST_SAVE_TRANSFER'}else{''}) + $(if($LobbyCompatibility){';LOBBY_COMPATIBILITY'}else{''}) + $(if($LairWoundedTest){';LAIR_RECOVERY_TEST'}else{''}) + $(if($LairRecovery){';LAIR_RECOVERY'}else{''})), ("/out:" + $exe))
    if($UniformUi){$arguments += '/define:PAW_UNIFORM_UI'}
    if($LargeMaps){$arguments += '/define:LARGE_MAPS'; $arguments += $largeMapsIdentity,(Join-Path $largeMapsDirectory 'ExtendedRegistryPatch.cs'),(Join-Path $registryNative 'ExtendedRegistryPayload.cs')}
    if($LocalLargeMapsSource){$arguments += '/define:LOCAL_LARGE_MAPS'; $arguments += [IO.Path]::GetFullPath($LocalLargeMapsSource)}
    if($CompanyPositionRecovery){$arguments += '/define:COMPANY_POSITION_RECOVERY';$arguments += (Join-Path $company 'CompanyPositionPatch.cs'),(Join-Path $companyNative 'CompanyPositionPayload.cs')}
    if($ExhaustionRecovery){$arguments += '/define:EXHAUSTION_RECOVERY';$arguments += (Join-Path $exhaustion 'ExhaustionRecoveryPatch.cs'),(Join-Path $exhaustionNative 'ExhaustionRecoveryPayload.cs')}
    if($AiPolicy){$arguments += '/define:AI_POLICY';$arguments += (Join-Path $ai 'AiPolicyRuntime.cs'),(Join-Path $ai 'PawAiOptions.cs'),(Join-Path $ai 'AiDiagnosticsSnapshot.cs'),(Join-Path $aiNative 'AiPolicyPayload.cs')}
    if($BotLobby){$arguments += '/define:BOT_LOBBY';$arguments += (Join-Path $botLobbySource 'BotLobbyPatch.cs'),(Join-Path $botLobbyNative 'BotLobbyPayload.cs')}
    if($NightmareDifficulty){$arguments += '/define:NIGHTMARE_DIFFICULTY';$arguments += (Join-Path $nightmareData 'NightmareDifficultyData.cs')}
    if($FractionalKingdomPoints){$arguments += '/define:FRACTIONAL_KINGDOM_POINTS';$arguments += (Join-Path $fractions 'FractionalPointsPatch.cs'),(Join-Path $fractionsNative 'FractionalPointsPayload.cs')}
    if($GraphicsDiagnostics){$arguments += '/define:GRAPHICS_DIAGNOSTICS';$arguments += (Join-Path $graphics 'GraphicsDiagnostics.cs');$arguments += '/resource:'+$graphicsExe+',PawsGraphicsRecorder'}
    if($SettlementSlots){$arguments += '/define:SETTLEMENT_SLOTS';$arguments += (Join-Path $slots 'SettlementSlotsPatch.cs'),(Join-Path $slotsNative 'SettlementSlotsPayload.cs')}
    if($CompanyBehavior){$arguments += '/define:COMPANY_BEHAVIOR';$arguments += (Join-Path $companyBehavior 'GroupRetreatPriorityPatch.cs'),(Join-Path $companyBehavior 'CompanyExhaustionPatch.cs'),(Join-Path $companyBehavior 'PawCompanyOptions.cs')}
    if($GroupAttackRecovery){$arguments += '/define:GROUP_ATTACK_RECOVERY';$arguments += (Join-Path $groupRecovery 'GroupAttackRecoveryPatch.cs'),(Join-Path $groupRecovery 'GroupMovementSpeedPatch.cs'),(Join-Path $groupSpeedNative 'GroupMovementSpeedPayload.cs')}
    if($SelectionScroll){$arguments += '/define:SELECTION_SCROLL';$arguments += (Join-Path $selection 'SelectionScrollPatch.cs'),(Join-Path $selectionNative 'SelectionScrollPayload.cs'),(Join-Path $selection 'GroupFormationPatch.cs'),(Join-Path $selectionNative 'GroupFormationPayload.cs'),(Join-Path $selection 'GroupFormationRadiusPatch.cs'),(Join-Path $selectionNative 'GroupFormationRadiusPayload.cs')}
    if($AllyEconomy){$arguments += '/define:ALLY_ECONOMY';$arguments += (Join-Path $ally 'AllyEconomyPatch.cs'),(Join-Path $allyNative 'AllyEconomyPayload.cs')}
    if($FoundationCounts){$arguments += '/define:FOUNDATION_COUNTS';$arguments += (Join-Path $foundation 'FoundationCountsPatch.cs'),(Join-Path $foundationNative 'FoundationCountsPayload.cs')}
    if($EngineCrashFixes){$arguments += '/define:ENGINE_CRASH_FIXES';$arguments += (Join-Path $crashFixes 'EngineCrashFixesPatch.cs'),(Join-Path $crashFixes 'TaskStatusPatch.cs'),(Join-Path $crashNative 'EngineCrashPayload.cs')}
    if($TerrainTexturePool){if(!$CameraZoom){throw 'Terrain texture pool requires the camera feature'};$arguments += '/define:TERRAIN_TEXTURE_POOL'}
    if($CameraZoom){$arguments += '/define:CAMERA_ZOOM_2';$arguments += (Join-Path $camera 'CameraZoomPatch.cs'),(Join-Path $cameraNative 'CameraZoomPayload.cs')}
    if($LairRecovery){$arguments += (Join-Path $lair 'LairRecoveryPatch.cs'),(Join-Path $lairNative 'LairRecoveryPayload.cs')}
    foreach ($resource in $resources) { $arguments += '/resource:' + (Join-Path $PSScriptRoot "$resource.bin") + ',' + $resource }
    $source = if ($variant.Name.StartsWith('k2_paws_lobby_colors_mp_nohostility')) { 'k2_paws_lobby_colors_mp_1372_experimental.cs' } else { [IO.Path]::GetFileNameWithoutExtension($variant.Name) + '.cs' }
    $sourcePath = Join-Path $PSScriptRoot $source
    if ($CityAssistant) {
        $text = [IO.File]::ReadAllText($sourcePath)
        $text = Replace-StartupAnchor $text 'if (args.Length == 1 && args[0] == "--features") return BuildFeatures.Write();' @'
if (args.Length == 1 && args[0] == "--assistant-self-test") return PawAssistantRuntime.SelfTest();
        if (args.Length == 1 && args[0] == "--fast-transfer-self-test") return FastTransferTests.Run(null);
        if (args.Length == 1 && args[0] == "--features") return BuildFeatures.Write();
'@
        $text = Replace-StartupAnchor $text 'ReleaseStartup.GuardData(root);' 'ReleaseStartup.GuardData(root); PawAssistantRuntime.GuardData(root);'
        $text = Replace-StartupAnchor $text 'startupComplete = true;' @'
PawAssistantRuntime.Install(game, imageBase, syncSignal, gameDirectory, delegate(string m) { AppendLog(logPath, m); });
            PawFastTransfer.Install(game, imageBase, delegate(string m) { AppendLog(logPath, m); });
#if GRAPHICS_DIAGNOSTICS
            GraphicsDiagnostics.Start(game, gameDirectory, delegate(string m) { AppendLog(logPath, m); });
#endif
            startupComplete = true;
'@
        $text = Replace-StartupAnchor $text 'current = ReadCounters(process, counters);' @'
PawAssistantRuntime.Tick();
                PawFastTransfer.Tick();
#if AI_POLICY
                AiPolicyRuntime.Tick();
#endif
#if ENGINE_CRASH_FIXES
                EngineCrashFixesPatch.Tick();
#endif
                current = ReadCounters(process, counters);
'@
        if($LobbyCompatibility) {
            $text=Replace-StartupAnchor $text 'PawAssistantRuntime.GuardData(root);' 'PawAssistantRuntime.GuardData(root); PawLobbyCompatibility.ValidateInstallation(root);'
            $text=Replace-StartupAnchor $text 'ReleaseStartup.GuardData(gameDirectory);' 'ReleaseStartup.GuardData(gameDirectory); PawLobbyCompatibility.Prepare(gameDirectory);'
            $text=Replace-StartupAnchor $text 'ReleaseStartup.InstallTerrainAndMap(game, imageBase, delegate(string m) { AppendLog(logPath, m); });' 'PawLobbyCompatibility.Install(game, imageBase, delegate(string m) { AppendLog(logPath, m); }); ReleaseStartup.InstallTerrainAndMap(game, imageBase, delegate(string m) { AppendLog(logPath, m); });'
            $arguments += '/r:System.Web.Extensions.dll'
            $arguments += $lobbySource
            $arguments += '/resource:'+(Join-Path $out 'paws_lobby_compatibility.dll')+',PawLobbyCompatibilityNative'
        }
        if($NightmareDifficulty){
            $text=Replace-StartupAnchor $text 'ReleaseStartup.GuardData(gameDirectory);' 'ReleaseStartup.GuardData(gameDirectory); NightmareDifficultyData.Prepare(gameDirectory,PawGameText.Language,PawAiOptions.Enabled);'
        }
        if($LairWoundedTest) {
            $text=Replace-StartupAnchor $text 'if (args.Length == 1 && args[0] == "--features") return BuildFeatures.Write();' @'
if (args.Length == 2 && args[0] == "--local-data-check") {
            try { ReleaseStartup.VerifyLocalLaunchData(args[1]); Console.WriteLine("LOCAL_DATA_PASS language="+PawGameText.Language); return 0; }
            catch(Exception error) { Console.Error.WriteLine(error.Message); return 2; }
        }
        if (args.Length == 1 && args[0] == "--features") return BuildFeatures.Write();
'@
            $text=Replace-StartupAnchor $text 'ReleaseStartup.GuardData(root);' 'ReleaseStartup.GuardData(root); ReleaseStartup.VerifyLocalLaunchData(root);'
            $text=Replace-StartupAnchor $text 'ReleaseStartup.GuardData(gameDirectory);' 'ReleaseStartup.GuardData(gameDirectory); ReleaseStartup.VerifyLocalLaunchData(gameDirectory);'
        }
        $sourcePath = Join-Path $out ($variant.Name + '.generated.cs')
        [IO.File]::WriteAllText($sourcePath,$text,[Text.UTF8Encoding]::new($false))
        $arguments += Join-Path $assistant 'PawAssistantRuntime.cs'
        $arguments += Join-Path $assistant 'PawAssistantParties.cs'
        $arguments += Join-Path $assistant 'CityPartyStore.cs'
        $arguments += Join-Path $assistant 'CityDevelopmentText.cs'
        $arguments += Join-Path $assistant 'CityPlanner.cs'
        $arguments += Join-Path $assistant 'CityMilitiaPlanner.cs'
        $arguments += Join-Path $assistant 'CityPolicy.cs'
        $arguments += Join-Path $assistant 'CityResourceOrder.cs'
        $arguments += Join-Path $assistant 'CitySettingsForm.cs'
        $arguments += Join-Path $transfer 'FastTransfer.cs'
        $arguments += Join-Path $transfer 'FastTransferTests.cs'
        $arguments += '/resource:' + (Join-Path $out 'FastTransferGuards.bin') + ',FastTransferGuards'
        foreach ($resource in 'AssistantPayload','AssistantFixups') { $arguments += '/resource:' + (Join-Path $assistant "$resource.bin") + ',' + $resource }
    }
    $arguments += $sourcePath
    foreach ($file in $shared) { $arguments += Join-Path $PSScriptRoot $file }
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw 'Helper compilation failed.' }
    $modes = @('--quiet-startup-self-test','--common-ui-self-test','--lobby-payload-test')
    if ($CityAssistant) { $modes += '--assistant-self-test','--fast-transfer-self-test' }
    foreach ($mode in $modes) {
        $test = Start-Process -FilePath $exe -ArgumentList $mode -WindowStyle Hidden -PassThru -Wait
        if ($test.ExitCode -ne 0) { throw "Offline test failed: $($variant.Name) $mode" }
    }
}
if ($CityAssistant) {
    & $python (Join-Path $PSScriptRoot 'test_saved_owners.py') --helpers $out --legacy $work
    if ($LASTEXITCODE -ne 0) { throw 'Saved owner regression failed.' }
    $layoutOut = Join-Path $out 'data/UI/Game'
    New-Item -ItemType Directory -Path $layoutOut -Force | Out-Null
    foreach($language in 'en','ru') {
        $file = "paw_city_$language.tgi"
        [IO.File]::WriteAllText((Join-Path $layoutOut $file),[IO.File]::ReadAllText((Join-Path $assistant $file)),[Text.Encoding]::Unicode)
        $localeDir = Join-Path $out $(if ($language -eq 'en') {'data/Localization'} else {'Local_ru/Localization'})
        New-Item -ItemType Directory -Path $localeDir -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $localeDir 'paw_city_policy.tgi'),[IO.File]::ReadAllText((Join-Path $assistant "localization_$language.tgi")),[Text.Encoding]::Unicode)
    }
    $queueTest = Join-Path $out 'CityPlannerTests.exe'
    & $compiler /nologo /target:exe /r:System.Core.dll "/out:$queueTest" (Join-Path $assistant 'CityPlanner.cs') (Join-Path $assistant 'CityPolicy.cs') (Join-Path $assistant 'CityResourceOrder.cs') (Join-Path $assistant 'CityPlannerTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'City planner test compilation failed.' }
    & $queueTest
    if ($LASTEXITCODE -ne 0) { throw 'City planner regression failed.' }
    $partyTest = Join-Path $out 'CityPartyStoreTests.exe'
    & $compiler /nologo /target:exe /r:System.Core.dll "/out:$partyTest" (Join-Path $assistant 'CityPartyStore.cs') (Join-Path $assistant 'CityPartyStoreTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'City party test compilation failed.' }
    & $partyTest
    if ($LASTEXITCODE -ne 0) { throw 'City party persistence regression failed.' }
    $militiaTest = Join-Path $out 'CityMilitiaPlannerTests.exe'
    & $compiler /nologo /target:exe /r:System.Core.dll "/out:$militiaTest" (Join-Path $assistant 'CityMilitiaPlanner.cs') (Join-Path $assistant 'CityMilitiaPlannerTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'City militia test compilation failed.' }
    & $militiaTest
    if ($LASTEXITCODE -ne 0) { throw 'City militia regression failed.' }
    $formTest = Join-Path $out 'CitySettingsFormTests.exe'
    & $compiler /nologo /target:exe /r:System.Core.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/out:$formTest" (Join-Path $PSScriptRoot 'GameText.cs') (Join-Path $assistant 'CityPlanner.cs') (Join-Path $assistant 'CityPolicy.cs') (Join-Path $assistant 'CitySettingsForm.cs') (Join-Path $assistant 'CitySettingsFormTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'City settings test compilation failed.' }
    & $formTest
    if ($LASTEXITCODE -ne 0) { throw 'City settings regression failed.' }
    foreach ($test in 'test_policy_native.py','test_construction_native.py','test_resource_inputs_native.py','test_automation_native.py','test_transport_native.py','test_preferences_native.py','test_markets_native.py') {
        & $python (Join-Path $assistant $test) --legacy (Join-Path $work 'city_assistant_1372') --native (Join-Path $out 'native')
        if ($LASTEXITCODE -ne 0) { throw "Native regression failed: $test" }
    }
    $transferTest = Join-Path $out 'FastTransferTests.exe'
    & $compiler /nologo /target:exe /platform:x86 /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$transferTest" ("/resource:" + (Join-Path $out 'FastTransferGuards.bin') + ',FastTransferGuards') (Join-Path $PSScriptRoot 'TerrainRuntime.cs') (Join-Path $transfer 'FastTransfer.cs') (Join-Path $transfer 'FastTransferTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Transfer test compilation failed.' }
    & $transferTest $out
    if ($LASTEXITCODE -ne 0) { throw 'Transfer regression failed.' }
    & $python (Join-Path $transfer 'test_native.py') --work $work --out $out
    if ($LASTEXITCODE -ne 0) { throw 'Native transfer regression failed.' }
}
Write-Output 'Built eight quiet helpers. No game launched. Install only with the matching signed release data packages.'
