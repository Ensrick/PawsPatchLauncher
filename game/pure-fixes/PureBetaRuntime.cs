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
        internal static bool MultiplayerRecoveryEnabled;
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
                + ",\"allyEconomyRevision\":2,\"gameplayCameraZoomMaximum\":2"
                + ",\"companyPositionRecoveryRevision\":1,\"exhaustionRecoveryRevision\":1"
                + ",\"bulkBotLobbyRevision\":3,\"aiPolicyRevision\":39,\"aiPolicyProfile\":\"pure\",\"aiPolicyCompatibilityRevision\":1,\"aiImprovementsSelectable\":true"
                + ",\"lobbyCompatibilityProtocol\":1,\"observerTeamCommandGuardRevision\":1,\"multiplayerRecoveryRevision\":1"
                + ",\"independentHostilitySelectable\":true,\"woundedLairDefendersSelectable\":false,\"woundedLairDefendersBuiltIn\":true"
                + ",\"builtInGraphicsDiagnosticsRevision\":1,\"goldSoundButton\":true"
                + ",\"automaticMilitia\":false,\"additionalBuildingSlots\":false"
                + ",\"foundationDistribution\":false,\"fractionalKingdomPoints\":false}";
        }
        internal static void ValidateInstallation(string root)
        {
            PawLobbyCompatibility.ValidateInstallation(root);
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
            ImprovedAi = PawAiOptions.Read(root);
            PawAiOptions.Enabled = ImprovedAi;
            WoundedDefenders = true;
            Hostility = ModuleEnabled(root,"pure-independent-hostility");
            MultiplayerRecoveryEnabled = ModuleEnabled(root,MultiplayerRecovery.ModuleId);
            NightmareDifficultyData.Validate(root, ImprovedAi);
            PawLobbyCompatibility.Prepare(root);
            if (MultiplayerRecoveryEnabled) MultiplayerRecovery.Prepare(root);
        }
        internal static void Validate(IMemory memory, uint image)
        {
            // Check every selected native module before the first write.
            CameraZoomPatch.Validate(memory, image);
            CompanyPositionPatch.Validate(memory, image);
            ExhaustionRecoveryPatch.Validate(memory, image);
            AllyEconomyPatch.Validate(memory, image);
            BotLobbyPatch.Validate(memory, image);
            EngineCrashFixesPatch.Validate(memory, image);
            RandomMapPatch.Validate(memory, image);
            if (WoundedDefenders) LairRecoveryPatch.Validate(memory, image);
            if (ImprovedAi) AiPolicyRuntime.Validate(memory, image);
        }
        internal static void Install(IMemory memory, uint image, uint registration,
            Process game, string root, Action<string> log)
        {
            CameraZoomPatch.Install(memory, image, log);
            CompanyPositionPatch.Install(memory, image, log);
            ExhaustionRecoveryPatch.Install(memory, image, log);
            AllyEconomyPatch.Install(memory, image, log);
            BotLobbyPatch.Install(memory, image, PawGameText.LanguageId, ImprovedAi, log);
            RandomMapPatch.Install(memory, image, log);
            if (WoundedDefenders) LairRecoveryPatch.Install(memory, image, log);
            uint crash = EngineCrashFixesPatch.Install(memory, image, registration, log);
            EngineCrashFixesPatch.Monitor(game.Id, crash, log);
            if (ImprovedAi) AiPolicyRuntime.Install(memory, image, game.Id, root, log);
        }
        internal static void Finish(Process game, IntPtr image, string root, Action<string> log)
        {
            PawLobbyCompatibility.Install(game, image, log);
            if (MultiplayerRecoveryEnabled) MultiplayerRecovery.Install(game, image, log);
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
