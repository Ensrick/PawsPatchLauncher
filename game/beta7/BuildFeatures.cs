using System;

internal static class BuildFeatures
{
    internal static int Write()
    {
#if PAW_CORELESS
        const bool commonFixes = false;
#else
        const bool commonFixes = true;
#endif
#if PAW_COLORS
        const bool colors = true;
#else
        const bool colors = false;
#endif
#if CITY_ASSISTANT
        const bool cityAssistant = true;
#else
        const bool cityAssistant = false;
#endif
#if FAST_SAVE_TRANSFER
        const bool fastSaveTransfer = true;
#else
        const bool fastSaveTransfer = false;
#endif
#if SYNC_CONTINUE
        const bool bypass = true;
        const string syncDiagnostics = ",\"syncDiagnosticsRevision\":1";
#else
        const bool bypass = false;
        const string syncDiagnostics = "";
#endif
#if SYNC_ONLY
        const bool hostility = false;
#else
        const bool hostility = true;
#endif
#if LOBBY_COMPATIBILITY
        const string lobby = ",\"lobbyCompatibility\":true,\"lobbyCompatibilityProtocol\":1,\"patchVersion\":\"" + PawLobbyCompatibility.Version + "\"";
#else
        const string lobby = "";
#endif
#if LAIR_RECOVERY
        const string lairRecovery = ",\"lairWoundedDefendersRevision\":3";
#else
        const string lairRecovery = "";
#endif
#if CAMERA_ZOOM_2
        const string camera = ",\"gameplayCameraRevision\":1,\"gameplayCameraZoomMaximum\":2,\"gameplayCameraFarPlane\":1024";
#else
        const string camera = "";
#endif
#if COMPANY_POSITION_RECOVERY
        const string companyPosition = ",\"companyPositionRecoveryRevision\":1";
#else
        const string companyPosition = "";
#endif
#if EXHAUSTION_RECOVERY
        const string exhaustion = ",\"exhaustionRecoveryRevision\":1";
#else
        const string exhaustion = "";
#endif
#if AI_POLICY
        const string ai = ",\"aiPolicyRevision\":39,\"builtInAiDiagnostics\":true,\"aiImprovementsSelectable\":true";
#else
        const string ai = "";
#endif
#if BOT_LOBBY
        const string botLobby = ",\"bulkBotLobbyRevision\":3";
#else
        const string botLobby = "";
#endif
#if FRACTIONAL_KINGDOM_POINTS
        const string fractions=",\"fractionalKingdomPointsRevision\":1";
#else
        const string fractions="";
#endif
#if GRAPHICS_DIAGNOSTICS
        const string graphics=",\"builtInGraphicsDiagnosticsRevision\":1";
#else
        const string graphics="";
#endif
#if SETTLEMENT_SLOTS
        const string settlementSlots=",\"settlementBuildingSlotsRevision\":2,\"settlementBuildingSlots\":8";
#else
        const string settlementSlots="";
#endif
#if ALLY_ECONOMY
        const string allyEconomy=",\"allyEconomyRevision\":2";
#else
        const string allyEconomy="";
#endif
#if FOUNDATION_COUNTS
        const string foundationCounts=",\"foundationDistributionRevision\":2,\"settlementCampPercent\":80,\"foundationCampPercent\":20";
#else
        const string foundationCounts="";
#endif
#if ENGINE_CRASH_FIXES
        const string crashFixes=",\"sharedAnimationTargetGuardRevision\":1,\"missingNetworkClientGuardRevision\":1,\"observerTeamCommandGuardRevision\":1";
#else
        const string crashFixes="";
#endif
#if NIGHTMARE_DIFFICULTY
        const string nightmare=",\"nightmareDifficultyRevision\":3,\"nightmareRequiresAiImprovements\":true";
#else
        const string nightmare="";
#endif
#if LARGE_MAPS
        const string largeMaps=",\"largeMapsRevision\":1,\"maximumMapSide\":2048,\"objectRegistryCapacity\":262143,\"terrainHashRevision\":1,\"largeMapWarningRevision\":1,\"minimapMarkerScaleRevision\":2";
#else
        const string largeMaps="";
#endif
#if PAW_UNIFORM_UI
        string uniformUi=commonFixes ? ",\"widescreenUniformUiRevision\":1" : "";
#else
        const string uniformUi="";
#endif
        Console.WriteLine("{\"colors\":" + colors.ToString().ToLowerInvariant() + largeMaps + uniformUi +
            ",\"bypass\":" + bypass.ToString().ToLowerInvariant() + syncDiagnostics +
            ",\"hostility\":" + hostility.ToString().ToLowerInvariant() +
            ",\"commonFixes\":" + commonFixes.ToString().ToLowerInvariant() + ",\"quiet\":true,\"cityAssistant\":" + cityAssistant.ToString().ToLowerInvariant() +
            ",\"advancedCityPolicy\":" + (cityAssistant && fastSaveTransfer).ToString().ToLowerInvariant() +
            ",\"cityPolicyRevision\":" + (cityAssistant ? "23" : "0") +
            ",\"nativeCityQueue\":" + cityAssistant.ToString().ToLowerInvariant() +
            ",\"automaticMines\":" + cityAssistant.ToString().ToLowerInvariant() +
            ",\"newCityMilitia\":" + cityAssistant.ToString().ToLowerInvariant() +
            ",\"startingCityMilitia\":" + cityAssistant.ToString().ToLowerInvariant() +
            ",\"fastSaveTransfer\":" + fastSaveTransfer.ToString().ToLowerInvariant() + lobby + lairRecovery + camera + companyPosition + exhaustion + ai + botLobby + fractions + graphics + settlementSlots + allyEconomy + crashFixes + foundationCounts + nightmare + "}");
        return 0;
    }
}
