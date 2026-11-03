using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace PawPureFixes
{
    // Separate composition root: no CityAssistant, automatic militia,
    // FoundationCounts, SettlementSlots or FractionalPoints code is linked.
    internal static class PureBetaRuntime
    {
        internal static bool ImprovedAi;
        internal static bool WoundedDefenders;
        internal static bool Hostility;
        internal static bool ModuleEnabled(string root, string id)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 16*1024*1024, RecursionLimit = 64 };
            var state = (Dictionary<string,object>)json.DeserializeObject(File.ReadAllText(Path.Combine(root,@".pawpatch\state.json")));
            var modules = (Dictionary<string,object>)state["modules"];
            object item, enabled;
            return modules.TryGetValue(id,out item) && ((Dictionary<string,object>)item).TryGetValue("enabled",out enabled) && Object.Equals(enabled,true);
        }
        internal static string Describe(string json)
        {
            return json.Replace("\"randomMap\":false","\"randomMap\":true").Replace("\"randomTime\":false","\"randomTime\":true").TrimEnd('}')
#if TERRAIN_TEXTURE_POOL
                + ",\"terrainTexturePoolEntries\":1024"
#endif
                + ",\"allyEconomyRevision\":2,\"gameplayCameraZoomMaximum\":2"
                + ",\"companyPositionRecoveryRevision\":1,\"exhaustionRecoveryRevision\":1"
#if COMPANY_BEHAVIOR
                + ",\"companyBehaviorRevision\":1,\"groupRetreatPriorityRevision\":1"
#endif
#if SELECTION_SCROLL
                + ",\"selectionScrollRevision\":3,\"selectionCardsAdaptive\":true,\"selectionDestinationFlagsAll\":true,\"groupFormationRevision\":1,\"groupFormationRadiusRevision\":1"
#endif
#if GROUP_ATTACK_RECOVERY
                + ",\"groupAttackRecoveryRevision\":1,\"groupMovementSpeedRevision\":2"
#endif
                + ",\"bulkBotLobbyRevision\":3,\"aiPolicyRevision\":39,\"aiPolicyProfile\":\"pure\",\"aiPolicyCompatibilityRevision\":1,\"aiImprovementsSelectable\":true"
                + ",\"lobbyCompatibilityProtocol\":1,\"observerTeamCommandGuardRevision\":1,\"taskStatusInitializationRevision\":1"
#if PAW_UNIFORM_UI
                + ",\"widescreenUniformUiRevision\":3"
#endif
#if PAW_PURE_MULTIPLAYER_SAVES
                + ",\"multiplayerSavesRevision\":2,\"multiplayerAutosaveMinutes\":5,\"clientSaveCopies\":true"
#endif
                + ",\"independentHostilitySelectable\":true,\"woundedLairDefendersSelectable\":false,\"woundedLairDefendersBuiltIn\":true"
                + ",\"builtInGraphicsDiagnosticsRevision\":3,\"animationStopControllerGuardRevision\":1,\"goldSoundButton\":true"
                + ",\"automaticMilitia\":false,\"additionalBuildingSlots\":false"
                + ",\"foundationDistribution\":false,\"fractionalKingdomPoints\":false}";
        }
        internal static void ValidateInstallation(string root)
        {
            PawLobbyCompatibility.ValidateInstallation(root);
            ReleaseStartup.ValidateTerrainTexturePool(root);
            string[] required={@"data\UI\Menus\main.tgi",@"data\UI\Menus\staging.tgi",@"data\UI\Game\game_interface.tgi",
                @"data\UI\Game\PawGoldSound.png",@"data\Audio\paws_gold_button.tgi",@"data\templates\template_rmc_k2.tgi",
                @"data\game\world_rules_k2.tgi",@"data\randommap\rmc_temperate03.tgi"};
            foreach(string path in required)
                if(!File.Exists(Path.Combine(root,path)))throw new InvalidDataException("Missing Pure patch data: "+path);
            if(!ModuleEnabled(root,"pure-beta-common") || !ModuleEnabled(root,"pure-fixes-runtime"))
                throw new InvalidDataException("Apply the complete Pure patch in the launcher.");
            NightmareDifficultyData.Validate(root,PawAiOptions.Read(root));
        }
        internal static void Prepare(string root)
        {
            ReleaseStartup.GameDataDirectory = Path.GetFullPath(root);
#if COMPANY_BEHAVIOR
            PawCompanyOptions.Read(root); // Reject an Arcane-only option in a Pure installation.
#endif
            ImprovedAi = PawAiOptions.Read(root);
            PawAiOptions.Enabled = ImprovedAi;
            WoundedDefenders = true;
            Hostility = ModuleEnabled(root,"pure-independent-hostility");
            NightmareDifficultyData.Validate(root, ImprovedAi);
            PawLobbyCompatibility.Prepare(root);
#if PAW_PURE_MULTIPLAYER_SAVES
            MultiplayerSaves.Prepare(root);
#endif
        }
        internal static void Validate(IMemory memory, uint image)
        {
            // Check every selected native module before the first write.
            CameraZoomPatch.Validate(memory, image);
            CompanyPositionPatch.Validate(memory, image);
#if COMPANY_BEHAVIOR
            GroupRetreatPriorityPatch.Validate(memory, image);
#endif
            ExhaustionRecoveryPatch.Validate(memory, image);
#if SELECTION_SCROLL
            SelectionScrollPatch.Validate(memory, image);
            GroupFormationPatch.Validate(memory, image);
            GroupFormationRadiusPatch.Validate(memory, image);
#endif
#if GROUP_ATTACK_RECOVERY
            GroupAttackRecoveryPatch.Validate(memory, image);
            GroupMovementSpeedPatch.Validate(memory, image);
#endif
            AllyEconomyPatch.Validate(memory, image);
            BotLobbyPatch.Validate(memory, image);
            EngineCrashFixesPatch.Validate(memory, image);
            TaskStatusPatch.Validate(memory, image);
            RandomMapPatch.Validate(memory, image);
            if (WoundedDefenders) LairRecoveryPatch.Validate(memory, image);
            if (ImprovedAi) AiPolicyRuntime.Validate(memory, image);
        }
        internal static void Install(IMemory memory, uint image, uint registration,
            Process game, string root, Action<string> log)
        {
            CameraZoomPatch.Install(memory, image, log);
            CompanyPositionPatch.Install(memory, image, log);
#if COMPANY_BEHAVIOR
            GroupRetreatPriorityPatch.Install(memory, image, log);
#endif
            ExhaustionRecoveryPatch.Install(memory, image, log);
#if SELECTION_SCROLL
            SelectionScrollPatch.Install(memory, image, log);
            GroupFormationPatch.Install(memory, image, log);
            GroupFormationRadiusPatch.Install(memory, image, log);
#endif
#if GROUP_ATTACK_RECOVERY
            GroupAttackRecoveryPatch.Install(memory, image, log);
            GroupMovementSpeedPatch.Install(memory, image, log);
#endif
            AllyEconomyPatch.Install(memory, image, log);
            BotLobbyPatch.Install(memory, image, PawGameText.LanguageId, ImprovedAi, log);
            RandomMapPatch.Install(memory, image, log);
            if (WoundedDefenders) LairRecoveryPatch.Install(memory, image, log);
            uint crash = EngineCrashFixesPatch.Install(memory, image, registration, log);
            EngineCrashFixesPatch.Monitor(game.Id, crash, log);
            TaskStatusPatch.Install(memory, image, log);
            if (ImprovedAi) AiPolicyRuntime.Install(memory, image, game.Id, root, log);
        }
        internal static void Finish(Process game, IntPtr image, string root, Action<string> log)
        {
            PawLobbyCompatibility.Install(game, image, log);
#if PAW_PURE_MULTIPLAYER_SAVES
            MultiplayerSaves.Install(game, image, log);
#endif
            if (Hostility)
            {
                using (var memory = new NativeMemory(game.Id,Path.Combine(root,"k2.exe"),game.StartTime.ToUniversalTime()))
                {
                    memory.Suspend();
                    try { K2PawFamilyPostgen1372.InstallPure(game,image,Path.Combine(root,Program.StatusFile)); }
                    finally { memory.Resume(); }
                }
            }
            GraphicsDiagnostics.Start(game, root, log);
        }
        internal static void Tick()
        {
            EngineCrashFixesPatch.Tick();
            if (ImprovedAi) AiPolicyRuntime.Tick();
        }
    }
}
