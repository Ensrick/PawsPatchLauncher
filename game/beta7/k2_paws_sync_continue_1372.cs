using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;

internal static class K2PawFamilyPostgen1372
{
    private const string ExpectedExeSha256 =
        "1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45";
    private const string ExpectedTemplateSha256 =
        ReleaseStartup.TemplateDataHash;
    private const string ExpectedEnglishSha256 =
        "52E58D16AE1B6B0DEAECE5EBD2EF6B294EC11E7C8C71D0EA40F760C4B546DACE";
    private const string ExpectedRussianSha256 =
        "E8641FE3543F4B4D34FF95874FE2EF9044381E68E026AE1CDE7222B91F3C2C2D";

#if SYNC_ONLY
    private const string WindowTitle =
        "Paw OPTIONAL Sync Bypass for Kohan II 1.3.72";
    private const string StatusFileName =
        "paws_sync_continue_1372_status.txt";
#elif HERD_RELATIONS_ONLY
#if SYNC_CONTINUE
    private const string WindowTitle =
        "Paw Family Kingdoms + Herd Relations + OPTIONAL Sync Bypass for Kohan II 1.3.72";
    private const string StatusFileName =
        "paws_sync_family_herd_relations_1372_status.txt";
#else
    private const string WindowTitle =
        "Paw Colors MP Experimental + Family/Herd 1.3.72 (Stock Sync Checks)";
    private const string StatusFileName =
        "paws_sync_continue_1372_status.txt";
#endif
#elif PROVOKED_NEUTRAL_ATTACK
    private const string WindowTitle =
        "Paw Experimental Provoked Neutral Sites for Kohan II 1.3.68";
    private const string StatusFileName =
        "paws_provoked_neutral_attack_experimental_status.txt";
#elif RACE_RELATIONS
    private const string WindowTitle =
        "Paw Experimental 37 Kingdoms V3 for Kohan II 1.3.68";
    private const string StatusFileName = "paws_kingdom37_experimental_v3_status.txt";
#elif SYNC_CONTINUE
    private const string WindowTitle =
        "Paw Sync + Family Kingdoms for Kohan II 1.3.68";
    private const string StatusFileName = "paws_sync_family_status.txt";
#else
    private const string WindowTitle =
        "Paw Family Kingdoms Experimental for Kohan II 1.3.68";
    private const string StatusFileName = "paws_family_postgen_status.txt";
#endif

    // Final kingdom-pointer store in the GActor materialization constructor in
    // Steam beta 1.3.72 (Steam build 25068126). The random-map generator has
    // already selected every actor and position before this instruction runs.
    // Replacing the owner only for exact known lair actor IDs here adds no
    // random calls and changes no actor groups, weights, spacing or placement order.
    private const int InitialKingdomStoreRva = 0x22C7CC;
    private const int MaterializedKingdomStoreRva = 0x22D00F;
    private const int GWorldPointerRva = 0x5F3FB8;

#if SYNC_CONTINUE
    private const int SyncFailureRva = 0x14A5F2;
    private const int LocalOutOfSyncMarkerRva = 0x14A4FE;
    private const int SyncFailureEhStateRva = 0x42EE6A;
    private const int SyncFailureNameRva = 0x4B7F44;
    private const int SyncFailureQualifiedNameRva = 0x4B7F64;
    private static readonly byte[] LocalOutOfSyncMarkerOriginal =
        { 0xC6, 0x40, 0x35, 0x01 };
    private static readonly byte[] LocalOutOfSyncMarkerSuppressed =
        { 0x90, 0x90, 0x90, 0x90 };
#endif

#if RACE_RELATIONS
    private const int SetRelationWrapperRva = 0x2352D4;
    // Direct call to SetRelationWrapper inside the stock routine that applies
    // the complete serialized kingdom-relation list. Multiplayer calls this
    // after actor creation. Hooking the call is safe for internal epilogue
    // entries; the worker runs only after the final list item was applied.
    private const int FinalRelationsSetCallRva = 0x259ACF;
    private const int RelationNeutral = 2;
    private const int RelationEnemy = 3;
    private static readonly byte[] FinalRelationsSetCallSignature =
    {
        0xE8, 0x00, 0xB8, 0xFD, 0xFF,
        0x47, 0x83, 0xC3, 0x08, 0x3B, 0x7E, 0x14, 0x7C, 0xA2,
    };
#endif

#if PROVOKED_NEUTRAL_ATTACK
    // KKC_TellActorAttackOrder's deterministic simulation-side handler. This
    // path is used by the explicit Attack command on every peer, unlike the
    // ordinary right-click movement path.
    private const int AttackOrderHookRva = 0x0D302E;
    // KKC_UIStateTeamCommandSelectTarget::IsValidTarget. The stock UI rejects
    // a neutral actor here and therefore never creates TellActorAttackOrder.
    // We retain the stock result and override only actors owned by one of the
    // six race-family kingdoms used by this experiment.
    private const int UiAttackTargetGateRva = 0x0D5FAD;
    // The first target gate accepts an otherwise valid neutral site, but the
    // command-specific validator can still return "target unavailable". This
    // is the post-call result branch in TeamCommandSelectTarget::PerformAction.
    private const int UiAttackCommandValidationRva = 0x0D5C7E;
    private const int UiResolveTargetActorRva = 0x2E5B69;
    private const int UiAttackCommandValidRva = 0x0D5D9E;
    private const int UiAttackCommandInvalidRva = 0x0D5C86;
    // The visible company Attack button is actually `ActorCommand kill` and
    // uses KKC_UIStateBrushActionSelectActor, not TeamCommandSelectTarget.
    // This is its stock relation-result branch after the target actor has
    // already been resolved into EDI. Stock accepts on ZF=1 and otherwise
    // displays the unavailable-target message.
    private const int UiActorCommandTargetGateRva = 0x0D574A;
    private const int UiActorCommandTargetValidRva = 0x0D5875;
    private const int UiActorCommandTargetInvalidRva = 0x0D5750;
    // KKC_UIStateBrushActionSelectActor reaches TellActorCommand only after a
    // second source-actor command validation.  The neutral relation is rejected
    // here even after the target-selection gate accepted the site.
    private const int ActorCommandDispatchValidationRva = 0x0D5071;
    private const int ActorCommandDispatchValidationSuccessRva = 0x0D5211;
    private const int ActorCommandDispatchValidationFailureRva = 0x0D5079;
    private const int ActorCommandDispatchFallbackValidationRva = 0x0D524A;
    private const int ActorCommandDispatchFallbackSuccessRva = 0x0D535E;
    private const int ActorCommandDispatchFallbackFailureRva = 0x0D5252;
    // ActorCommand kill is transported through KKC_TellActorCommandOrder, not
    // KKC_TellActorAttackOrder.  Hook its deterministic execution routine
    // before the stock target validation so every peer changes an explicitly
    // provoked neutral site to the same reserve kingdom.
    private const int ActorCommandExecuteRva = 0x0D39CE;
    // Actor commands are copied into a common serialized base object before
    // remote execution.  The derived `kill` vtable and registry descriptor are
    // not stable at this point, but the stock SetTarget method always preserves
    // bit zero in command+0x0C together with the target actor ID at +0x10.
    private const int ActorLookupManagerRva = 0x5D372C;
    private const int ActorLookupByIdRva = 0x021117;
    // GKingdom::GetRelationTo.  Stock collapses playable kingdoms to their
    // shared team parent.  The detour keeps stock diplomacy as the fallback,
    // but handles race-family neutrality and herd hostility against the
    // actual playable kingdom that owns the querying actor.
    private const int KingdomGetRelationRva = 0x23A3CE;
    // RMinimap's final local-player fallback. Stock 1.3.68 deliberately
    // returns false for an independent neutral kingdom, which hides our newly
    // neutral race-family sites. The detour returns the already prepared stock
    // neutral colour only for those six family kingdoms.
    private const int MinimapNeutralFamilyGateRva = 0x2D719C;
    private const int MinimapNeutralFamilySuccessRva = 0x2D70AA;
    private const int MinimapNeutralFamilyBlendRva = 0x2D71A9;
    private const int MinimapNeutralFamilyFalseRva = 0x2D71AD;
    private const int AttackOrderContextRva = 0x5DD1E0;
    private const int ActorSetKingdomRva = 0x2285A8;
    private const int ReserveFirstKingdomIndex = 23;
    private const int ReserveKingdomCount = 12;
    private const int RequiredCompleteKingdomCount = 37;
#endif

    private const int CounterEntries = 0;
    private const int CounterCandidates = 1;
    private const int CounterMappedActors = 2;
    private const int CounterRemappedActors = 3;
    private const int CounterMissingKingdoms = 4;
    private const int CounterInitialConstructor = 5;
    private const int CounterMaterializedConstructor = 6;
#if RACE_RELATIONS
    private const int CounterRacePasses = 7;
    private const int CounterRacePlayers = 8;
    private const int CounterRaceNeutralCalls = 9;
    private const int CounterRaceMissingFamilies = 10;
    private const int CounterFinalRelationPasses = 11;
#if PROVOKED_NEUTRAL_ATTACK
    private const int CounterAttackOrders = 12;
    private const int CounterEligibleNeutralTargets = 13;
    private const int CounterProvokedAssignments = 14;
    private const int CounterExistingProvokedOrders = 15;
    private const int CounterProvokedSlotsExhausted = 16;
    private const int CounterSetKingdomFailures = 17;
    private const int CounterUiAttackOverrides = 18;
    private const int CounterMinimapNeutralFamilyIcons = 19;
    private const int CounterUiCommandValidationOverrides = 20;
    private const int CounterUiGateEntries = 21;
    private const int CounterUiGateStockRejected = 22;
    private const int CounterUiCommandValidationEntries = 23;
    private const int CounterUiCommandValidationErrors = 24;
    private const int CounterUiCommandKind4Errors = 25;
    private const int CounterUiCommandResolvedActors = 26;
    private const int CounterUiCommandFamilyTargets = 27;
    private const int CounterLastUiCommandError = 28;
    private const int CounterLastUiCommandKind = 29;
    private const int CounterUiActorTargetGateEntries = 30;
    private const int CounterUiActorTargetStockRejected = 31;
    private const int CounterUiActorTargetFamilyOverrides = 32;
    private const int CounterActorCommandOrders = 33;
    private const int CounterActorCommandKillOrders = 34;
    private const int CounterActorCommandTargetsResolved = 35;
    private const int CounterPersonalRelationQueries = 36;
    private const int CounterRaceNeutralOverrides = 37;
    private const int CounterHerdEnemyOverrides = 38;
    private const int CounterProvokedEnemyOverrides = 39;
    private const int CounterUiImmediateProvocationAttempts = 40;
    private const int CounterUiImmediateProvocationAssignments = 41;
    private const int CounterCount = 42;
#else
    private const int CounterCount = 12;
#endif
#else
    private const int CounterCount = 7;
#endif

    private static readonly string[,] ActorKingdomMap =
    {
        // Regular independent cities. The exact settlement actor and all five
        // centers in its Levels list belong to the same racial family. Map
        // the settlement container too so its center, structures, denizens and
        // shared wall set inherit one owner. Do not map walls by race globally:
        // those assets are shared with keeps and unrelated special sites.
        // PRESERVE_NONINDEPENDENT_OWNER below protects every playable/captured
        // owner; these IDs do not transfer player starting cities to families.
        { "human_settlement", "paws_war_human" },
        { "human_center_village", "paws_war_human" },
        { "human_center_town", "paws_war_human" },
        { "human_center_city", "paws_war_human" },
        { "human_center_citadel", "paws_war_human" },
        { "human_center_citadel_militia", "paws_war_human" },
        { "haroun_settlement", "paws_war_haroun" },
        { "haroun_center_village", "paws_war_haroun" },
        { "haroun_center_town", "paws_war_haroun" },
        { "haroun_center_city", "paws_war_haroun" },
        { "haroun_center_citadel", "paws_war_haroun" },
        { "haroun_center_citadel_militia", "paws_war_haroun" },
        { "drauga_settlement", "paws_war_drauga" },
        { "drauga_center_village", "paws_war_drauga" },
        { "drauga_center_town", "paws_war_drauga" },
        { "drauga_center_city", "paws_war_drauga" },
        { "drauga_center_citadel", "paws_war_drauga" },
        { "drauga_center_citadel_militia", "paws_war_drauga" },
        { "gauri_settlement", "paws_war_gauri" },
        { "gauri_center_village", "paws_war_gauri" },
        { "gauri_center_town", "paws_war_gauri" },
        { "gauri_center_city", "paws_war_gauri" },
        { "gauri_center_citadel", "paws_war_gauri" },
        { "gauri_center_citadel_militia", "paws_war_gauri" },
        { "undead_settlement", "paws_war_undead" },
        { "undead_center_village", "paws_war_undead" },
        { "undead_center_town", "paws_war_undead" },
        { "undead_center_city", "paws_war_undead" },
        { "undead_center_citadel", "paws_war_undead" },
        { "undead_center_citadel_militia", "paws_war_undead" },
        { "shadow_settlement", "paws_war_dark_rift" },
        { "shadow_center_village", "paws_war_dark_rift" },
        { "shadow_center_town", "paws_war_dark_rift" },
        { "shadow_center_city", "paws_war_dark_rift" },
        { "shadow_center_citadel", "paws_war_dark_rift" },
        { "shadow_center_citadel_militia", "paws_war_dark_rift" },

        { "bandit_foundationcamp", "kingdom_indie" },
        { "bandit_settlementcamp", "kingdom_indie" },
        { "lair_bandit_hideout", "kingdom_indie" },
        { "lair_bandit_lair", "kingdom_indie" },
        { "lair_bandit_redoubt", "kingdom_indie" },
        { "barbarian_foundationcamp", "paws_war_barbarian" },
        { "barbarian_settlementcamp", "paws_war_barbarian" },
        { "lair_barbarian_camp", "paws_war_barbarian" },
        { "lair_barbarian_encampment", "paws_war_barbarian" },
        { "lair_barbarian_garrison", "paws_war_barbarian" },
        { "branch_thing_dwelling", "paws_war_branch" },
        { "active_lair_dragon_lair", "paws_war_fire_dragon" },
        { "active_ice_dragon_lair", "paws_war_ice_dragon" },
        { "night_lurkerers_cavern", "paws_war_night" },

        // Human technological sites. Special-settlement spot ownership is
        // changed before it materializes the center and any shared wall set.
        { "tech_lair_journeyman_guild", "paws_war_human" },
        { "journeyman_guild_spot", "paws_war_human" },
        { "tech_lair_magetower", "paws_war_human" },
        { "gathering_of_light", "paws_war_human" },
        { "gathering_of_light_spot", "paws_war_human" },
        { "tower_of_conjuration", "paws_war_human" },
        { "tower_of_conjuration_spot", "paws_war_human" },

        // Haroun technological sites.
        { "tech_lair_wildwood", "paws_war_haroun" },
        { "Spirit_Tree", "paws_war_haroun" },
        { "spirit_tree_spot", "paws_war_haroun" },

        // Undead technological site.  Its wall set inherits the spot owner.
        { "ancient_laygrounds", "paws_war_undead" },
        { "ancient_laygrounds_spot", "paws_war_undead" },

        // Drauga and Gauri technological sites.
        { "tech_lair_encampment", "paws_war_drauga" },
        { "tech_lair_watchtower", "paws_war_gauri" },

        // Shadow technological sites. Their shared stock wall sets inherit
        // the owner from the remapped special-settlement spot.
        { "gathering_of_dark", "paws_war_dark_rift" },
        { "gathering_of_dark_spot", "paws_war_dark_rift" },
        { "tech_lair_Golden_hoard", "paws_war_dark_rift" },
        { "golden_hoard_spot", "paws_war_dark_rift" },
        { "Shadow_Sepulcher", "paws_war_dark_rift" },

        { "lair_rhakshahive", "paws_war_rhaksha" },
        { "lair_rhakshanest", "paws_war_rhaksha" },
        { "rhaksha_enclave_center", "paws_war_rhaksha" },
        { "rhaksha_enclave_spot", "paws_war_rhaksha" },
        { "rhaksha_foundationcamp", "paws_war_rhaksha" },
        { "rhaksha_settlementcamp", "paws_war_rhaksha" },
        { "scorpion_nest", "paws_war_scorpion" },
        { "scorpion_nest_arctic", "paws_war_scorpion" },
        { "scorpion_nest_grim", "paws_war_scorpion" },
        { "scorpion_nest_temperate", "paws_war_scorpion" },
        { "slaan_foundationcamp", "paws_war_slaan" },
        { "slaan_settlementcamp", "paws_war_slaan" },
        { "slaanri_enclave_center", "paws_war_slaan" },
        { "slanrii_enclave_spot", "paws_war_slaan" },
        { "tech_lair_kraal", "paws_war_slaan" },
        { "lair_spider_lair", "paws_war_spider" },
        { "lair_spider_lair_arctic", "paws_war_spider" },
        { "lair_spider_lair_desert", "paws_war_spider" },
        { "lair_spider_lair_grim", "paws_war_spider" },
        { "active_storm_drake_crag", "paws_war_storm" },
        { "wyvern_nest", "paws_war_storm" },
        { "haunted_ruin_lich", "paws_war_undead" },
        { "Cursed_city", "paws_war_undead" },
        { "cursed_city_settlement", "paws_war_undead" },
        { "lair_dark_rift", "paws_war_dark_rift" },
        { "lair_hauntedruin", "paws_war_undead" },
        { "dire_wolf_den", "paws_war_wolf" },
        { "snow_wolf_den", "paws_war_wolf" },
    };

    // The exact staged template hash fixes this order in GWorld's complete
    // kingdom array: two playable slots, herd, bandits, generic enemies,
    // capturable independents, thirteen monster families, then four racial
    // families. The random-map template deliberately has 37 total kingdoms.
    private static int GetTargetKingdomIndex(string kingdomIds)
    {
        switch (kingdomIds)
        {
            case "kingdom_indie": return 3;
            case "paws_war_barbarian": return 6;
            case "paws_war_branch": return 7;
            case "paws_war_fire_dragon": return 8;
            case "paws_war_ice_dragon": return 9;
            case "paws_war_storm": return 10;
            case "paws_war_wolf": return 11;
            case "paws_war_spider": return 12;
            case "paws_war_scorpion": return 13;
            case "paws_war_night": return 14;
            case "paws_war_rhaksha": return 15;
            case "paws_war_undead": return 16;
            case "paws_war_slaan": return 17;
            case "paws_war_dark_rift": return 18;
            case "paws_war_human": return 19;
            case "paws_war_haroun": return 20;
            case "paws_war_drauga": return 21;
            case "paws_war_gauri": return 22;
            default: throw new InvalidOperationException(
                "Unknown target kingdom IDS: " + kingdomIds);
        }
    }

    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint PageReadWrite = 0x04;
    private const uint PageExecuteReadWrite = 0x40;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(
        IntPtr process, IntPtr address, int size, uint allocationType, uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtectEx(
        IntPtr process, IntPtr address, int size, uint newProtection, out uint oldProtection);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushInstructionCache(
        IntPtr process, IntPtr address, int size);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--features") return BuildFeatures.Write();
        if (args.Length == 1 && args[0] == "--quiet-startup-self-test") return ReleaseStartup.SelfTest();
        if (args.Length == 2 && args[0] == "--preflight")
        { string root = Path.GetFullPath(args[1]); VerifyFiles(root, Path.Combine(root, "k2.exe")); ReleaseStartup.GuardData(root); Console.WriteLine("PREFLIGHT_PASS " + ReleaseStartup.Build); return 0; }
        if (args.Length == 1 && args[0] == "--lobby-payload-test")
            return PawLobbyColorsNative.VerifyOffline();
        if (args.Length == 1 && args[0] == "--common-ui-self-test")
            return PawGamePresentation.VerifyOffline();
        if (args.Any(a => a.StartsWith("--attach") || a == "--replace-secondary"))
            throw new InvalidOperationException("The color prototype only supports a fresh game process.");
        bool replaceSecondary = args.Any(a =>
            a.Equals("--replace-secondary", StringComparison.OrdinalIgnoreCase));
        bool attachSecondaryOnly = replaceSecondary || args.Any(a =>
            a.Equals("--attach-secondary", StringComparison.OrdinalIgnoreCase));
        bool attach = attachSecondaryOnly || args.Any(a =>
            a.Equals("--attach", StringComparison.OrdinalIgnoreCase));
        bool test = args.Any(a => a.Equals("--test", StringComparison.OrdinalIgnoreCase));
        bool migrateOnly = args.Any(a =>
            a.Equals("--migrate-only", StringComparison.OrdinalIgnoreCase));
        string gameDirectory = ReleaseStartup.GameDirectory(args.Where(a => a != "--test").ToArray());
        string gamePath = Path.Combine(gameDirectory, "k2.exe");
        string logPath = Path.Combine(gameDirectory, StatusFileName);
        Process game = null;
        bool startupComplete = false;
        IntPtr process = IntPtr.Zero;
        IntPtr syncSignal = IntPtr.Zero;
        IntPtr raceRelationWorker = IntPtr.Zero;
#if PROVOKED_NEUTRAL_ATTACK
        IntPtr provokedState = IntPtr.Zero;
        IntPtr provokedHostility = IntPtr.Zero;
        IntPtr provokedHostilityWorker = IntPtr.Zero;
        IntPtr provokedRelationWorker = IntPtr.Zero;
#endif

        try
        {
            VerifyFiles(gameDirectory, gamePath);
            ReleaseStartup.GuardData(gameDirectory);
            AppendLog(logPath, "QUIET_START " + ReleaseStartup.Build + "; startupWindow=false; helperChild=false;");
#if !SYNC_ONLY
            MigratePersistedShadowKingdomId(logPath);
#endif
            if (migrateOnly)
                return 0;

            Process[] running = Process.GetProcessesByName("k2");
            if (attach)
            {
                if (running.Length != 1)
                    throw new InvalidOperationException(
                        "Для --attach должна быть запущена ровно одна копия k2.exe.");
                game = running[0];
            }
            else
            {
                if (running.Length != 0)
                    throw new InvalidOperationException(
                        "Сначала закройте уже запущенную Kohan II, затем запускайте игру через этот файл.");
                ProcessStartInfo start = new ProcessStartInfo(gamePath)
                {
                    WorkingDirectory = gameDirectory,
                    UseShellExecute = true,
                };
                DateTime launch = DateTime.UtcNow;
                game = Process.Start(start);
                if (game == null)
                    throw new InvalidOperationException("Не удалось запустить k2.exe.");
                IntPtr verifiedImage;
                using (Process initial = game) game = ReleaseStartup.WaitForGame(gamePath, launch, out verifiedImage);
            }

            IntPtr imageBase = ReleaseStartup.VerifiedImage;
            process = OpenProcess(
                ProcessVmOperation | ProcessVmRead | ProcessVmWrite |
                ProcessQueryInformation,
                false,
                game.Id);
            if (process == IntPtr.Zero)
                ThrowWin32("OpenProcess");

            // Validate every active site before installing any detour. Steam
            // decrypts the executable during startup; the primary build does
            // not have the old sync-hook wait to serialize that startup.
#if !SYNC_ONLY
            WaitForInitialKingdomStoreCode(process,
                Add(imageBase, InitialKingdomStoreRva),
                new byte[] { 0x89, 0x87, 0xE8, 0, 0, 0, 0x85, 0xC0, 0x75, 0x1D },
                game, 60000);
            WaitForInitialKingdomStoreCode(process,
                Add(imageBase, MaterializedKingdomStoreRva),
                new byte[] { 0x89, 0x87, 0xE8, 0, 0, 0, 0xEB, 0x09 },
                game, 60000);
            WaitForInitialKingdomStoreCode(process,
                Add(imageBase, KingdomGetRelationRva),
                new byte[] { 0x8B, 0xC1, 0x8B, 0x4C, 0x24, 0x04 },
                game, 60000);
#endif
#if SYNC_CONTINUE
            WaitForSyncFailureCode(process, imageBase,
                Add(imageBase, SyncFailureRva), game, 60000);
            if (!ReadBytes(process, Add(imageBase, LocalOutOfSyncMarkerRva), 4)
                .SequenceEqual(LocalOutOfSyncMarkerOriginal))
                throw new InvalidOperationException("OOS marker signature mismatch before patching.");
            AppendLog(logPath, "MODE optional-sync-bypass; stockChecksumChecks=true; nativeFailureHandling=false; firstFailureNativeLog=true; notifications=false.");
#else
            AppendLog(logPath, "MODE primary; stockSyncChecks=true; syncBypass=false.");
#endif

#if SYNC_ONLY
            AppendLog(logPath, "BUILD 1.3.72-sync-only-r1; familyAssignment=false; independentHostility=false.");
#else
            AppendLog(logPath, "BUILD 1.3.72-city-families-r3; saved-owners=preserved; saved-session-remap=false; regular-independent-cities-by-race=true; cityMappings=36; newHookSites=0; newAiControllers=false.");
#endif
            ReleaseStartup.InstallTerrainAndMap(game, imageBase, delegate(string m) { AppendLog(logPath, m); });
            PawGamePresentation.Install(process, imageBase, logPath);
#if PAW_COLORS
            PawLobbyColorsNative.Install(process, imageBase, logPath);
#endif
            IntPtr counters = VirtualAllocEx(
                process,
                IntPtr.Zero,
                CounterCount * 4,
                MemCommit | MemReserve,
                PageReadWrite);
            if (counters == IntPtr.Zero)
                ThrowWin32("VirtualAllocEx(counters)");
            WriteBytes(process, counters, new byte[CounterCount * 4]);

#if SYNC_CONTINUE
            syncSignal = InstallSyncBypass(
                game, process, imageBase, logPath);
#endif

#if RACE_RELATIONS
#if !PROVOKED_NEUTRAL_ATTACK
            raceRelationWorker = CreateRaceRelationsWorker(
                process, imageBase, counters, logPath);
            InstallFinalRelationsHook(
                process, imageBase, counters, raceRelationWorker, logPath);
#endif
#endif

#if PROVOKED_NEUTRAL_ATTACK
#if HERD_RELATIONS_ONLY
            InstallPersonalKingdomRelationHook(
                process,
                imageBase,
                counters,
                IntPtr.Zero,
                IntPtr.Zero,
                logPath);
#else
            provokedState = VirtualAllocEx(
                process,
                IntPtr.Zero,
                ReserveKingdomCount * 8,
                MemCommit | MemReserve,
                PageReadWrite);
            if (provokedState == IntPtr.Zero)
                ThrowWin32("VirtualAllocEx(provoked state)");
            WriteBytes(
                process,
                provokedState,
                new byte[ReserveKingdomCount * 8]);
            provokedHostility = VirtualAllocEx(
                process,
                IntPtr.Zero,
                ReserveKingdomCount * 8,
                MemCommit | MemReserve,
                PageReadWrite);
            if (provokedHostility == IntPtr.Zero)
                ThrowWin32("VirtualAllocEx(provoked hostility)");
            WriteBytes(
                process,
                provokedHostility,
                new byte[ReserveKingdomCount * 8]);
            provokedRelationWorker = CreateProvokedRelationWorker(
                process, imageBase, counters, logPath);
            provokedHostilityWorker = CreateProvokedHostilityWorker(
                process,
                imageBase,
                provokedHostility,
                logPath);
            IntPtr uiImmediateProvocationWorker = CreateDirectProvocationWorker(
                process,
                imageBase,
                counters,
                provokedState,
                provokedHostilityWorker,
                provokedRelationWorker,
                logPath);
            InstallPersonalKingdomRelationHook(
                process,
                imageBase,
                counters,
                provokedState,
                provokedHostility,
                logPath);
            InstallExplicitAttackProvocationHook(
                process,
                imageBase,
                counters,
                provokedState,
                provokedHostilityWorker,
                provokedRelationWorker,
                logPath);
            InstallActorCommandKillProvocationHook(
                process,
                imageBase,
                counters,
                provokedState,
                provokedHostilityWorker,
                provokedRelationWorker,
                logPath);
            InstallUiNeutralFamilyAttackGateHook(
                process,
                imageBase,
                counters,
                logPath);
            InstallUiNeutralFamilyCommandValidationHook(
                process,
                imageBase,
                counters,
                logPath);
            InstallUiActorCommandNeutralFamilyTargetHook(
                process,
                imageBase,
                counters,
                uiImmediateProvocationWorker,
                logPath);
            InstallActorCommandDispatchNeutralFamilyValidationHook(
                process,
                imageBase,
                counters,
                logPath);
            InstallActorCommandDispatchFallbackNeutralFamilyValidationHook(
                process,
                imageBase,
                counters,
                logPath);
            InstallMinimapNeutralFamilyVisibilityHook(
                process,
                imageBase,
                counters,
                logPath);
#endif
#endif

#if !SYNC_ONLY
            int[] siteRvas = attachSecondaryOnly
                ? new int[] { MaterializedKingdomStoreRva }
                : new int[] { InitialKingdomStoreRva, MaterializedKingdomStoreRva };
            string[] siteNames = attachSecondaryOnly
                ? new string[] { "materialized-constructor" }
                : new string[] { "initial-constructor", "materialized-constructor" };
            int[] siteCounters = attachSecondaryOnly
                ? new int[] { CounterMaterializedConstructor }
                : new int[] { CounterInitialConstructor, CounterMaterializedConstructor };
            byte[][] siteSignatures = attachSecondaryOnly
                ? new byte[][]
                {
                    new byte[] { 0x89, 0x87, 0xE8, 0, 0, 0, 0xEB, 0x09 },
                }
                : new byte[][]
                {
                    new byte[] { 0x89, 0x87, 0xE8, 0, 0, 0, 0x85, 0xC0, 0x75, 0x1D },
                    new byte[] { 0x89, 0x87, 0xE8, 0, 0, 0, 0xEB, 0x09 },
                };

            for (int siteIndex = 0; siteIndex < siteRvas.Length; ++siteIndex)
            {
                IntPtr target = Add(imageBase, siteRvas[siteIndex]);
                byte[] original;
                if (replaceSecondary)
                {
                    original = new byte[] { 0x89, 0x87, 0xE8, 0, 0, 0 };
                }
                else
                {
                    byte[] signature = WaitForInitialKingdomStoreCode(
                        process, target, siteSignatures[siteIndex], game, 60000);
                    original = signature.Take(6).ToArray();
                }
                IntPtr stub = VirtualAllocEx(
                    process,
                    IntPtr.Zero,
                    32768,
                    MemCommit | MemReserve,
                    PageExecuteReadWrite);
                if (stub == IntPtr.Zero)
                    ThrowWin32("VirtualAllocEx(stub)");

                byte[] stubCode = BuildFamilyOwnerStub(
                    imageBase,
                    target,
                    original,
                    counters,
                    stub,
                    siteCounters[siteIndex],
                    raceRelationWorker);
                WriteBytes(process, stub, stubCode);
                if (!FlushInstructionCache(process, stub, stubCode.Length))
                    ThrowWin32("FlushInstructionCache(stub)");

                byte[] detour = new byte[5];
                detour[0] = 0xE9;
                Buffer.BlockCopy(
                    BitConverter.GetBytes(RelativeBranch(target, stub)),
                    0,
                    detour,
                    1,
                    4);
                WriteCodePatch(
                    process,
                    target,
                    detour,
                    siteNames[siteIndex] + " kingdom-store detour");

                AppendLog(
                    logPath,
                    "PATCHED site=" + siteNames[siteIndex] +
                    " pid=" + game.Id +
                    " base=0x" + imageBase.ToInt64().ToString("X8") +
                    " target=0x" + target.ToInt64().ToString("X8") +
                    " stub=0x" + stub.ToInt64().ToString("X8") +
                    " counters=0x" + counters.ToInt64().ToString("X8") +
                    " mappings=" + ActorKingdomMap.GetLength(0) +
                    " original=" + BitConverter.ToString(original).Replace('-', ' ') +
                    " mode=exact-lair-postgen-reassignment; saved-owners=preserved; saved-session-remap=false" +
#if PRESERVE_NONINDEPENDENT_OWNER
                    " preserve-captured-owner=true" +
#endif
                    " generation-groups=unchanged.");
            }
#endif

            startupComplete = true;
            if (test)
            {
#if SYNC_CONTINUE
                WriteInt32(process, Add(syncSignal, 4), unchecked((int)0x13572468));
                WriteInt32(process, syncSignal, 1);
                VerifyOneSyncSignal(process, syncSignal, logPath);
#endif
                int[] values = ReadCounters(process, counters);
                AppendLog(
                    logPath,
#if SYNC_ONLY
                    "SELF-TEST PASSED: syncHookInstalled=true familyHookInstalled=false gameAlive=" + !HasExited(game) +
#else
                    "SELF-TEST PASSED: hookInstalled=true gameAlive=" + !HasExited(game) +
#endif
                    " counters=" + FormatCounters(values) + ".");
                return 0;
            }

            AppendLog(logPath, "QUIET_READY " + ReleaseStartup.Build + "; pid=" + game.Id + "; startupWindow=false;");
            MonitorRuntime(game, process, counters, syncSignal, logPath);
            return 0;
        }
        catch (Exception error)
        {
            if (!startupComplete)
                try { ReleaseStartup.StopIncompleteLaunch(game, delegate(string m) { AppendLog(logPath, m); }); }
                catch (Exception cleanup) { try { AppendLog(logPath, "CLEANUP_ERROR " + cleanup); } catch { } }
            try { AppendLog(logPath, "ERROR " + error); } catch { }
            MessageBox.Show(
                error.Message,
                WindowTitle,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (process != IntPtr.Zero)
                CloseHandle(process);
            if (game != null)
                game.Dispose();
        }
    }

    private static void MigratePersistedShadowKingdomId(string logPath)
    {
        string preferencesPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Kohan2", "data", "User", "Preferences.rup");
        if (!File.Exists(preferencesPath))
            return;

        byte[] oldId = Encoding.Unicode.GetBytes("paws_war_shadow");
        byte[] stableId = Encoding.Unicode.GetBytes("paws_war_night");
        byte[] source = File.ReadAllBytes(preferencesPath);
        List<int> offsets = new List<int>();
        for (int offset = 2; offset <= source.Length - oldId.Length; ++offset)
        {
            bool match = source[offset - 2] == 15 && source[offset - 1] == 0;
            for (int index = 0; match && index < oldId.Length; ++index)
                match = source[offset + index] == oldId[index];
            if (match)
                offsets.Add(offset);
        }
        if (offsets.Count == 0)
            return;

        List<byte> migrated = new List<byte>(source);
        for (int index = offsets.Count - 1; index >= 0; --index)
        {
            int offset = offsets[index];
            migrated[offset - 2] = 14;
            migrated[offset - 1] = 0;
            migrated.RemoveRange(offset, oldId.Length);
            migrated.InsertRange(offset, stableId);
        }

        string backupPath = preferencesPath + ".before_paws_shadow_id_migration";
        if (!File.Exists(backupPath))
            File.Copy(preferencesPath, backupPath, false);
        string temporaryPath = preferencesPath + ".paws_migration.tmp";
        File.WriteAllBytes(temporaryPath, migrated.ToArray());
        File.Replace(temporaryPath, preferencesPath, null);
        AppendLog(
            logPath,
            "MIGRATED persisted scenario kingdom id paws_war_shadow -> " +
            "paws_war_night occurrences=" + offsets.Count + ".");
    }

    private static void VerifyFiles(string gameDirectory, string gamePath)
    {
        if (!File.Exists(gamePath))
            throw new FileNotFoundException(
                "Поместите лаунчер в папку Kohan II рядом с k2.exe.", gamePath);
        VerifyHash(gamePath, ExpectedExeSha256, "k2.exe Steam beta 1.3.72");
        VerifyHash(
            Path.Combine(gameDirectory, "Data", "Templates", "template_rmc_k2.tgi"),
            ExpectedTemplateSha256,
            "семейные королевства");
        VerifyHash(
            Path.Combine(gameDirectory, "Data", "Localization", "strings_data_K2.tgi"),
            ExpectedEnglishSha256,
            "английская локализация семей");
        // The thin English package has no Russian localization. Family IDs
        // and simulation data are validated above; RU labels are optional.
        string russianPath = Path.Combine(gameDirectory,
            "Local_ru", "Localization", "strings_data_K2.tgi");
        if (File.Exists(russianPath))
            VerifyHash(russianPath, ExpectedRussianSha256, "русская локализация семей");
    }

    private static void VerifyHash(string path, string expected, string label)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Не найден обязательный файл: " + label, path);
        string actual = ComputeSha256(path);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Не совпал файл: " + label + "\r\n" +
                "Expected SHA-256: " + expected + "\r\n" +
                "Actual SHA-256:   " + actual);
    }

    private static byte[] WaitForInitialKingdomStoreCode(
        IntPtr process,
        IntPtr target,
        byte[] expected,
        Process game,
        int timeoutMilliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        byte[] last = null;
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (HasExited(game))
                throw new InvalidOperationException("Kohan II закрылась до установки патча.");
            try
            {
                last = ReadBytes(process, target, expected.Length);
                if (last.SequenceEqual(expected))
                    return last;
            }
            catch (Win32Exception)
            {
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException(
            "Не совпала сигнатура начального назначения королевства для 1.3.68. " +
            "Последние байты: " +
            (last == null ? "недоступны" : BitConverter.ToString(last)));
    }

#if SYNC_CONTINUE
    private static IntPtr InstallSyncBypass(
        Process game,
        IntPtr process,
        IntPtr imageBase,
        string logPath)
    {
        IntPtr target = Add(imageBase, SyncFailureRva);
        byte[] original = WaitForSyncFailureCode(
            process, imageBase, target, game, 60000);
        IntPtr localOutOfSyncMarker = Add(imageBase, LocalOutOfSyncMarkerRva);
        byte[] markerOriginal = ReadBytes(
            process, localOutOfSyncMarker, LocalOutOfSyncMarkerOriginal.Length);
        if (!markerOriginal.SequenceEqual(LocalOutOfSyncMarkerOriginal))
            throw new InvalidOperationException(
                "Не совпала сигнатура локального флага рассинхрона: " +
                BitConverter.ToString(markerOriginal));

        IntPtr reset = Add(imageBase, PawSyncDiagnostics.ResetRva);
        byte[] resetOriginal = ReadBytes(process, reset, PawSyncDiagnostics.ResetSignature.Length);
        if (!resetOriginal.SequenceEqual(PawSyncDiagnostics.ResetSignature))
            throw new InvalidOperationException("Network synchronizer reset signature mismatch.");

        IntPtr signal = VirtualAllocEx(
            process, IntPtr.Zero, PawSyncDiagnostics.SignalSize, MemCommit | MemReserve, PageReadWrite);
        if (signal == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(sync signal)");
        WriteBytes(process, signal, PawSyncDiagnostics.InitialSignal());

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, PawSyncDiagnostics.CodeSize,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(sync stub)");
        byte[] stubCode = PawSyncDiagnostics.Build(unchecked((uint)imageBase.ToInt64()),
            unchecked((uint)stub.ToInt64()), unchecked((uint)signal.ToInt64()));
        WriteBytes(process, stub, stubCode);
        if (!FlushInstructionCache(process, stub, stubCode.Length))
            ThrowWin32("FlushInstructionCache(sync stub)");

        byte[] detour = new byte[5];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)), 0, detour, 1, 4);
        byte[] resetDetour = new byte[5]; resetDetour[0] = 0xE9;
        Buffer.BlockCopy(BitConverter.GetBytes(RelativeBranch(reset,
            Add(stub, PawSyncDiagnostics.ResetOffset))), 0, resetDetour, 1, 4);
        try
        {
            WriteCodePatch(process, reset, resetDetour, "Sync diagnostic baseline observer");
            WriteCodePatch(process, target, detour, "SyncFailure detour");
            WriteCodePatch(
                process,
                localOutOfSyncMarker,
                LocalOutOfSyncMarkerSuppressed,
                "local out-of-sync marker suppression");
        }
        catch
        {
            try
            {
                WriteCodePatch(
                    process, localOutOfSyncMarker, markerOriginal,
                    "local out-of-sync marker rollback");
            }
            catch { }
            try
            {
                WriteCodePatch(
                    process, target, original.Take(detour.Length).ToArray(),
                    "SyncFailure detour rollback");
            }
            catch { }
            try { WriteCodePatch(process, reset, resetOriginal.Take(5).ToArray(),
                "Sync diagnostic baseline rollback"); } catch { }
            throw;
        }

        AppendLog(
            logPath,
            "SYNC PATCHED pid=" + game.Id +
            " target=0x" + target.ToInt64().ToString("X8") +
            " localOosMarker=0x" + localOutOfSyncMarker.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " signal=0x" + signal.ToInt64().ToString("X8") +
            " notifications=false firstFailureNativeLog=true diagnosticsRevision=1 gameContinues=true.");
        return signal;
    }

    private static byte[] WaitForSyncFailureCode(
        IntPtr process,
        IntPtr imageBase,
        IntPtr target,
        Process game,
        int timeoutMilliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        byte[] last = null;
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (HasExited(game))
                throw new InvalidOperationException(
                    "Kohan II закрылась до установки обхода рассинхрона.");
            try
            {
                last = ReadBytes(process, target, 31);
                if (MatchesSyncFailure(last, imageBase))
                    return last;
            }
            catch (Win32Exception) { }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException(
            "Код SyncFailure для 1.3.72 не появился за отведённое время. " +
            "Последние байты: " +
            (last == null ? "недоступны" : BitConverter.ToString(last)));
    }

    private static bool MatchesSyncFailure(byte[] code, IntPtr imageBase)
    {
        if (code == null || code.Length < 31)
            return false;
        if (code[0] != 0xB8 ||
            code[5] != 0xE8 ||
            code[6] != 0xE0 || code[7] != 0x6F || code[8] != 0x29 || code[9] != 0x00 ||
            code[10] != 0x83 || code[11] != 0xEC || code[12] != 0x0C ||
            code[13] != 0x53 || code[14] != 0x56 || code[15] != 0x57 ||
            code[16] != 0xFF || code[17] != 0x75 || code[18] != 0x08 ||
            code[19] != 0x8B || code[20] != 0xF9 ||
            code[21] != 0x68 || code[26] != 0x68)
            return false;

        uint baseValue = unchecked((uint)imageBase.ToInt64());
        return BitConverter.ToUInt32(code, 1) == baseValue + SyncFailureEhStateRva &&
            BitConverter.ToUInt32(code, 22) == baseValue + SyncFailureNameRva &&
            BitConverter.ToUInt32(code, 27) == baseValue + SyncFailureQualifiedNameRva;
    }

#endif

#if RACE_RELATIONS
    private static IntPtr CreateRaceRelationsWorker(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 4096,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(race relation stub)");
        byte[] stubCode = BuildRaceRelationWorker(
            imageBase, counters, stub);
        WriteBytes(process, stub, stubCode);
        if (!FlushInstructionCache(process, stub, stubCode.Length))
            ThrowWin32("FlushInstructionCache(race relation stub)");
        AppendLog(
            logPath,
            "RACE RELATIONS WORKER READY stub=0x" +
            stub.ToInt64().ToString("X8") +
            " neutral=2 mappings=human->human,haroun->haroun," +
            "undead->undead,shadow->shadows,drauga->drauga,gauri->gauri " +
            "player-relations=stock-parent-aware " +
            "triggers=mapped-lair+after-stock-relation-list " +
            "generation=unchanged.");
        return stub;
    }

    private static void InstallFinalRelationsHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        IntPtr raceRelationWorker,
        string logPath)
    {
        IntPtr target = Add(imageBase, FinalRelationsSetCallRva);
        byte[] actual = ReadBytes(
            process, target, FinalRelationsSetCallSignature.Length);
        if (!actual.SequenceEqual(FinalRelationsSetCallSignature))
            throw new InvalidOperationException(
                "Не совпала сигнатура финального применения дипломатии 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 128,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(final relations hook)");
        byte[] stubCode = BuildFinalRelationsCallHook(
            imageBase,
            target,
            counters,
            stub,
            raceRelationWorker);
        WriteBytes(process, stub, stubCode);
        if (!FlushInstructionCache(process, stub, stubCode.Length))
            ThrowWin32("FlushInstructionCache(final relations hook)");

        byte[] detour = new byte[5];
        detour[0] = 0xE8;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0,
            detour,
            1,
            4);
        WriteCodePatch(
            process,
            target,
            detour,
            "final stock-relation-list SetRelation call detour");
        AppendLog(
            logPath,
            "FINAL RELATIONS HOOK PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " trigger=after-final-stock-relation-item neutral-reapply=true.");
    }

    private static byte[] BuildFinalRelationsCallHook(
        IntPtr imageBase,
        IntPtr target,
        IntPtr counters,
        IntPtr stub,
        IntPtr raceRelationWorker)
    {
        if (raceRelationWorker == IntPtr.Zero)
            throw new InvalidOperationException(
                "Race-relation worker is unavailable.");

        List<byte> code = new List<byte>();
        code.AddRange(new byte[] { 0x83, 0xC4, 0x04 });                        // discard call-site return
        int stockCall = AddCall(code);                                        // original SetRelationWrapper call
        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        code.AddRange(new byte[] { 0x8B, 0x04, 0x24 });                        // mov eax,[esp] saved EDI index
        code.Add(0x40);                                                        // inc eax (next index)
        code.AddRange(new byte[] { 0x8B, 0x54, 0x24, 0x04 });                  // mov edx,[esp+4] saved ESI list
        code.AddRange(new byte[] { 0x3B, 0x42, 0x14 });                        // cmp eax,[edx+14] count
        int notFinalJump = AddConditionalJump(code, 0x85);                    // jne restore
        AddCounterIncrement(code, counters, CounterFinalRelationPasses);
        int workerCall = AddCall(code);
        int restoreOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int returnJump = AddJump(code);                                       // resume after patched call

        PatchCall(code, stub, stockCall, Add(imageBase, SetRelationWrapperRva));
        PatchCall(code, stub, workerCall, raceRelationWorker);
        PatchConditionalJump(code, stub, notFinalJump, Add(stub, restoreOffset));
        PatchJump(code, stub, returnJump, Add(target, 5));
        return code.ToArray();
    }

    private static byte[] BuildRaceRelationWorker(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> finishJumps = new List<int>();
        List<int> nextPlayerJumps = new List<int>();
        List<int> compareCalls = new List<int>();
        List<int> humanJumps = new List<int>();
        List<int> harounJumps = new List<int>();
        List<int> undeadJumps = new List<int>();
        List<int> shadowJumps = new List<int>();
        List<int> draugaJumps = new List<int>();
        List<int> gauriJumps = new List<int>();
        List<int> familyApplyCalls = new List<int>();
        List<StringReference> strings = new List<StringReference>();

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterRacePasses);

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });                              // test eax,eax
        finishJumps.Add(AddConditionalJump(code, 0x84));                       // jz finish
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0, 0x17 });     // cmp [eax+154],23
        int missingInitialJump = AddConditionalJump(code, 0x82);              // jb missing
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });           // mov ebx,[eax+150]
        code.AddRange(new byte[] { 0x85, 0xDB });                              // test ebx,ebx
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0x7B, 0x40, 0 });                     // undead family [16]
        int missingUndeadJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x83, 0x7B, 0x48, 0 });                     // shadows family [18]
        int missingShadowJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x83, 0x7B, 0x4C, 0 });                     // human family [19]
        int missingHumanJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x83, 0x7B, 0x50, 0 });                     // haroun family [20]
        int missingHarounJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x83, 0x7B, 0x54, 0 });                     // drauga family [21]
        int missingDraugaJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x83, 0x7B, 0x58, 0 });                     // gauri family [22]
        int missingGauriJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x33, 0xF6 });                              // xor esi,esi

        int loopOffset = code.Count;
        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0xB0, 0x54, 0x01, 0, 0 });           // cmp esi,[eax+154]
        finishJumps.Add(AddConditionalJump(code, 0x83));                       // jae finish
        code.AddRange(new byte[] { 0x8B, 0x2C, 0xB3 });                        // mov ebp,[ebx+esi*4]
        code.AddRange(new byte[] { 0x85, 0xED });
        nextPlayerJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x95, 0x40, 0x02, 0, 0 });           // mov edx,[ebp+240]
        code.AddRange(new byte[] { 0x85, 0xD2 });
        nextPlayerJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x52, 0x08 });                        // mov edx,[nation+8] IDS
        code.AddRange(new byte[] { 0x85, 0xD2 });
        nextPlayerJumps.Add(AddConditionalJump(code, 0x84));
        AddCounterIncrement(code, counters, CounterRacePlayers);

        humanJumps.Add(AddRaceCompare(
            code, "human", strings, compareCalls));
        humanJumps.Add(AddRaceCompare(
            code, "Human", strings, compareCalls));
        harounJumps.Add(AddRaceCompare(
            code, "haroun", strings, compareCalls));
        harounJumps.Add(AddRaceCompare(
            code, "Haroun", strings, compareCalls));
        undeadJumps.Add(AddRaceCompare(
            code, "undead", strings, compareCalls));
        undeadJumps.Add(AddRaceCompare(
            code, "Undead", strings, compareCalls));
        shadowJumps.Add(AddRaceCompare(
            code, "shadow", strings, compareCalls));
        shadowJumps.Add(AddRaceCompare(
            code, "Shadow", strings, compareCalls));
        draugaJumps.Add(AddRaceCompare(
            code, "drauga", strings, compareCalls));
        draugaJumps.Add(AddRaceCompare(
            code, "Drauga", strings, compareCalls));
        gauriJumps.Add(AddRaceCompare(
            code, "gauri", strings, compareCalls));
        gauriJumps.Add(AddRaceCompare(
            code, "Gauri", strings, compareCalls));
        nextPlayerJumps.Add(AddJump(code));

        int humanOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x7B, 0x4C });                        // mov edi,[ebx+19*4]
        familyApplyCalls.Add(AddCall(code));
        int humanNextJump = AddJump(code);

        int harounOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x7B, 0x50 });                        // mov edi,[ebx+20*4]
        familyApplyCalls.Add(AddCall(code));
        int harounNextJump = AddJump(code);

        int undeadOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x7B, 0x40 });                        // mov edi,[ebx+16*4]
        familyApplyCalls.Add(AddCall(code));
        int undeadNextJump = AddJump(code);

        int shadowOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x7B, 0x48 });                        // mov edi,[ebx+18*4]
        familyApplyCalls.Add(AddCall(code));
        int shadowNextJump = AddJump(code);

        int draugaOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x7B, 0x54 });                        // mov edi,[ebx+21*4]
        familyApplyCalls.Add(AddCall(code));
        int draugaNextJump = AddJump(code);

        int gauriOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x7B, 0x58 });                        // mov edi,[ebx+22*4]
        familyApplyCalls.Add(AddCall(code));
        int gauriNextJump = AddJump(code);

        int nextOffset = code.Count;
        code.Add(0x46);                                                        // inc esi
        int loopJump = AddJump(code);

        int globalMissingOffset = code.Count;
        AddCounterIncrement(code, counters, CounterRaceMissingFamilies);
        int globalMissingFinishJump = AddJump(code);

        int finishOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        code.Add(0xC3);                                                        // ret

        // Internal helper: EDI is the selected family and EBP is the playable
        // kingdom. It exactly mirrors the stock parent-aware relation call.
        int applyRelationOffset = code.Count;
        code.AddRange(new byte[] { 0x85, 0xFF });                              // test edi,edi
        int missingApplyJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x8B, 0x85, 0xF8, 0x01, 0, 0 });           // mov eax,[ebp+1F8] player relation target
        code.AddRange(new byte[] { 0x85, 0xC0 });                              // test eax,eax
        int directPlayerJump = AddConditionalJump(code, 0x84);                // jz direct player
        code.AddRange(new byte[] { 0x6A, RelationNeutral });                   // push NEUTRAL
        code.Add(0x50);                                                        // push player relation target
        code.AddRange(new byte[] { 0x8B, 0xCF });                              // mov ecx,edi family
        int parentSetRelationCall = AddCall(code);
        int appliedCounterJump = AddJump(code);

        int directPlayerOffset = code.Count;
        code.AddRange(new byte[] { 0x6A, RelationNeutral });                   // push NEUTRAL
        code.Add(0x57);                                                        // push target family
        code.AddRange(new byte[] { 0x8B, 0xCD });                              // mov ecx,ebp player
        int directSetRelationCall = AddCall(code);

        int appliedCounterOffset = code.Count;
        AddCounterIncrement(code, counters, CounterRaceNeutralCalls);
        code.Add(0xC3);                                                        // ret

        int missingOffset = code.Count;
        AddCounterIncrement(code, counters, CounterRaceMissingFamilies);
        code.Add(0xC3);                                                        // ret

        int compareOffset = AppendWideStringCompare(code);
        Dictionary<string, int> stringOffsets = new Dictionary<string, int>(
            StringComparer.Ordinal);
        foreach (StringReference reference in strings)
        {
            int stringOffset;
            if (!stringOffsets.TryGetValue(reference.Value, out stringOffset))
            {
                stringOffset = code.Count;
                stringOffsets.Add(reference.Value, stringOffset);
                code.AddRange(Encoding.Unicode.GetBytes(reference.Value + "\0"));
            }
            PatchInt32(code, reference.ImmediateOffset, Add(stub, stringOffset).ToInt32());
        }

        IntPtr compareAddress = Add(stub, compareOffset);
        foreach (int call in compareCalls)
            PatchCall(code, stub, call, compareAddress);
        foreach (int jump in humanJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, humanOffset));
        foreach (int jump in harounJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, harounOffset));
        foreach (int jump in undeadJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, undeadOffset));
        foreach (int jump in shadowJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, shadowOffset));
        foreach (int jump in draugaJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, draugaOffset));
        foreach (int jump in gauriJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, gauriOffset));
        foreach (int jump in nextPlayerJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, Add(stub, nextOffset));
            else
                PatchConditionalJump(code, stub, jump, Add(stub, nextOffset));
        }
        foreach (int jump in finishJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, finishOffset));
        foreach (int call in familyApplyCalls)
            PatchCall(code, stub, call, Add(stub, applyRelationOffset));

        PatchConditionalJump(code, stub, missingInitialJump, Add(stub, globalMissingOffset));
        PatchConditionalJump(code, stub, missingUndeadJump, Add(stub, globalMissingOffset));
        PatchConditionalJump(code, stub, missingShadowJump, Add(stub, globalMissingOffset));
        PatchConditionalJump(code, stub, missingHumanJump, Add(stub, globalMissingOffset));
        PatchConditionalJump(code, stub, missingHarounJump, Add(stub, globalMissingOffset));
        PatchConditionalJump(code, stub, missingDraugaJump, Add(stub, globalMissingOffset));
        PatchConditionalJump(code, stub, missingGauriJump, Add(stub, globalMissingOffset));
        PatchJump(code, stub, humanNextJump, Add(stub, nextOffset));
        PatchJump(code, stub, harounNextJump, Add(stub, nextOffset));
        PatchJump(code, stub, undeadNextJump, Add(stub, nextOffset));
        PatchJump(code, stub, shadowNextJump, Add(stub, nextOffset));
        PatchJump(code, stub, draugaNextJump, Add(stub, nextOffset));
        PatchJump(code, stub, gauriNextJump, Add(stub, nextOffset));
        PatchConditionalJump(code, stub, missingApplyJump, Add(stub, missingOffset));
        PatchConditionalJump(code, stub, directPlayerJump, Add(stub, directPlayerOffset));
        PatchCall(code, stub, parentSetRelationCall, Add(imageBase, SetRelationWrapperRva));
        PatchJump(code, stub, appliedCounterJump, Add(stub, appliedCounterOffset));
        PatchCall(code, stub, directSetRelationCall, Add(imageBase, SetRelationWrapperRva));
        PatchJump(code, stub, loopJump, Add(stub, loopOffset));
        PatchJump(code, stub, globalMissingFinishJump, Add(stub, finishOffset));
        return code.ToArray();
    }

#if PROVOKED_NEUTRAL_ATTACK
    private static IntPtr CreateProvokedRelationWorker(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 8192,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(provoked relation worker)");
        byte[] code = BuildProvokedRelationWorker(imageBase, counters, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(provoked relation worker)");
        AppendLog(
            logPath,
            "PROVOKED RELATION WORKER READY stub=0x" +
            stub.ToInt64().ToString("X8") +
            " bytes=" + code.Length +
            " policy=reserve-to-original-family-neutral;" +
            " herd=enemy-for-undead-shadow-otherwise-neutral;" +
            " player-specific-policy=GetRelationTo-detour.");
        return stub;
    }

    // Custom stdcall helper: (reserve kingdom, original family).  The
    // family-to-reserve relation is neutral.  The herd-to-reserve relation
    // remains hostile for former Undead and Shadow holdings, while all other
    // provoked holdings remain neutral to animals.
    // Player-specific relations are
    // supplied by the GetRelationTo detour, because the stock relation writer
    // collapses allied players to one shared team parent.
    private static byte[] BuildProvokedRelationWorker(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> finishJumps = new List<int>();
        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        code.AddRange(new byte[] { 0x8B, 0x7C, 0x24, 0x28 });                  // mov edi,[esp+40] reserve
        code.AddRange(new byte[] { 0x8B, 0x6C, 0x24, 0x2C });                  // mov ebp,[esp+44] family
        code.AddRange(new byte[] { 0x85, 0xFF });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x85, 0xED });
        finishJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x6A, RelationNeutral });                   // reserve <-> original family
        code.Add(0x55);
        code.AddRange(new byte[] { 0x8B, 0xCF });
        int familyNeutralCall = AddCall(code);
        AddCounterIncrement(code, counters, CounterRaceNeutralCalls);

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0, 0x03 });     // count >=3
        finishJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x90, 0x50, 0x01, 0, 0 });           // kingdoms
        code.AddRange(new byte[] { 0x85, 0xD2 });
        finishJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0xBB, RelationNeutral, 0, 0, 0 });         // default herd relation
        code.AddRange(new byte[] { 0x3B, 0x6A, 0x40 });                        // family==Undead (index 16)
        int undeadEnemyJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x3B, 0x6A, 0x48 });                        // family==Shadow (index 18)
        int nonShadowJump = AddConditionalJump(code, 0x85);
        int enemyRelationOffset = code.Count;
        code.AddRange(new byte[] { 0xBB, RelationEnemy, 0, 0, 0 });           // Undead/Shadow attack herd
        int herdRelationReadyOffset = code.Count;

        code.AddRange(new byte[] { 0x8B, 0x52, 0x08 });                        // herd kingdom index 2
        code.AddRange(new byte[] { 0x85, 0xD2 });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0x53);                                                        // selected herd relation
        code.Add(0x52);
        code.AddRange(new byte[] { 0x8B, 0xCF });
        int herdNeutralCall = AddCall(code);

        int finishOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        code.AddRange(new byte[] { 0xC2, 0x08, 0x00 });                        // ret 8

        foreach (int jump in finishJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, finishOffset));
        PatchConditionalJump(code, stub, undeadEnemyJump, Add(stub, enemyRelationOffset));
        PatchConditionalJump(code, stub, nonShadowJump, Add(stub, herdRelationReadyOffset));
        PatchCall(code, stub, familyNeutralCall, Add(imageBase, SetRelationWrapperRva));
        PatchCall(code, stub, herdNeutralCall, Add(imageBase, SetRelationWrapperRva));
        return code.ToArray();
    }

    private static IntPtr CreateProvokedHostilityWorker(
        IntPtr process,
        IntPtr imageBase,
        IntPtr hostility,
        string logPath)
    {
        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 1024,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(provoked hostility worker)");
        byte[] code = BuildProvokedHostilityWorker(imageBase, hostility, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(provoked hostility worker)");
        AppendLog(
            logPath,
            "PROVOKED HOSTILITY WORKER READY stub=0x" +
            stub.ToInt64().ToString("X8") +
            " table=0x" + hostility.ToInt64().ToString("X8") +
            " policy=per-playable-kingdom-bitset.");
        return stub;
    }

    // Custom stdcall helper: (reserve slot, attacking playable kingdom).
    private static byte[] BuildProvokedHostilityWorker(
        IntPtr imageBase,
        IntPtr hostility,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> finishJumps = new List<int>();
        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        code.AddRange(new byte[] { 0x8B, 0x74, 0x24, 0x28 });                  // mov esi,[esp+40] slot
        code.AddRange(new byte[] { 0x8B, 0x7C, 0x24, 0x2C });                  // mov edi,[esp+44] player
        code.AddRange(new byte[] { 0x83, 0xFE, ReserveKingdomCount });
        finishJumps.Add(AddConditionalJump(code, 0x83));                       // jae finish
        code.AddRange(new byte[] { 0x85, 0xFF });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });           // mov ebx,[world+150]
        code.AddRange(new byte[] { 0x85, 0xDB });
        finishJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x33, 0xC9 });                              // xor ecx,ecx
        int loopOffset = code.Count;
        code.Add(0xA1);
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x3B, 0x88, 0x54, 0x01, 0, 0 });           // cmp ecx,[world+154]
        finishJumps.Add(AddConditionalJump(code, 0x83));
        code.AddRange(new byte[] { 0x3B, 0x3C, 0x8B });                        // cmp edi,[ebx+ecx*4]
        int foundJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);                                                        // inc ecx
        int repeatJump = AddJump(code);

        int foundOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xC6 });                              // mov eax,esi
        code.AddRange(new byte[] { 0xC1, 0xE0, 0x03 });                        // shl eax,3
        code.Add(0x05);                                                        // add eax,hostility
        code.AddRange(BitConverter.GetBytes(hostility.ToInt32()));
        code.AddRange(new byte[] { 0x83, 0xF9, 0x20 });                        // cmp ecx,32
        int lowWordJump = AddConditionalJump(code, 0x82);
        code.AddRange(new byte[] { 0x83, 0xE9, 0x20 });                        // sub ecx,32
        code.AddRange(new byte[] { 0x83, 0xC0, 0x04 });                        // add eax,4
        int lowWordOffset = code.Count;
        code.AddRange(new byte[] { 0x0F, 0xAB, 0x08 });                        // bts [eax],ecx

        int finishOffset = code.Count;
        code.Add(0x61);
        code.Add(0x9D);
        code.AddRange(new byte[] { 0xC2, 0x08, 0x00 });                        // ret 8
        foreach (int jump in finishJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, finishOffset));
        PatchConditionalJump(code, stub, foundJump, Add(stub, foundOffset));
        PatchJump(code, stub, repeatJump, Add(stub, loopOffset));
        PatchConditionalJump(code, stub, lowWordJump, Add(stub, lowWordOffset));
        return code.ToArray();
    }

    private static void InstallPersonalKingdomRelationHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        IntPtr state,
        IntPtr hostility,
        string logPath)
    {
        IntPtr target = Add(imageBase, KingdomGetRelationRva);
        byte[] expected = { 0x8B, 0xC1, 0x8B, 0x4C, 0x24, 0x04 };
        byte[] original = ReadBytes(process, target, expected.Length);
        if (!original.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала функция личной дипломатии 1.3.72: " +
                BitConverter.ToString(original));
        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 8192,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(personal kingdom relation stub)");
        byte[] code = BuildPersonalKingdomRelationStub(
            imageBase, target, original, counters, state, hostility, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(personal kingdom relation stub)");
        byte[] detour = new byte[expected.Length];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        detour[5] = 0x90;
        WriteCodePatch(process, target, detour, "personal kingdom relations detour");
        AppendLog(
            logPath,
            "PERSONAL KINGDOM RELATIONS PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
#if HERD_RELATIONS_ONLY
            " policy=undead-shadow-vs-herd-enemy-only; " +
            "race-neutrality=false; provoked-neutral-attack=false.");
#else
            " policy=actual-player-race-neutral+undead-shadow-vs-herd-enemy+" +
            "per-player-provoked-hostility.");
#endif
    }

    private static byte[] BuildPersonalKingdomRelationStub(
        IntPtr imageBase,
        IntPtr target,
        byte[] original,
        IntPtr counters,
        IntPtr state,
        IntPtr hostility,
        IntPtr stub)
    {
#if HERD_RELATIONS_ONLY
        return BuildHerdOnlyKingdomRelationStub(
            imageBase, target, original, counters, stub);
#else
        List<byte> code = new List<byte>();
        List<int> stockJumps = new List<int>();
        List<int> compareCalls = new List<int>();
        List<StringReference> strings = new List<StringReference>();

        code.Add(0x9C);
        code.Add(0x60);
        AddCounterIncrement(code, counters, CounterPersonalRelationQueries);
        code.AddRange(new byte[] { 0x8B, 0x6C, 0x24, 0x18 });                  // ebp=saved ecx self
        code.AddRange(new byte[] { 0x8B, 0x74, 0x24, 0x28 });                  // esi=other argument
        code.AddRange(new byte[] { 0x85, 0xED });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x85, 0xF6 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0xEE });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0xA1);
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0,
            RequiredCompleteKingdomCount });
        stockJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });           // ebx=complete kingdoms
        code.AddRange(new byte[] { 0x85, 0xDB });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        // Reserve kingdom on either side: use its private hostility bitset,
        // then fall back to same-race neutrality.
        code.AddRange(new byte[] { 0x33, 0xC9 });                              // ecx=slot
        int reserveLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x3B, 0x6C, 0x8B,
            ReserveFirstKingdomIndex * 4 });
        int selfReserveJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x3B, 0x74, 0x8B,
            ReserveFirstKingdomIndex * 4 });
        int otherReserveJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int reserveRepeatJump = AddConditionalJump(code, 0x8C);
        int noReserveJump = AddJump(code);

        int selfReserveOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xFE });                              // edi=other player
        int reserveCommonJump = AddJump(code);
        int otherReserveOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xFD });                              // edi=self player
        int reserveCommonOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x14, 0xCD });                        // edx=state[slot].family
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xD2 });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x33, 0xC0 });                              // eax=player index
        int playerLoopOffset = code.Count;
        code.Add(0x3B);
        code.AddRange(new byte[] { 0x84, 0x83, 0x00, 0x00, 0x00, 0x00 });     // cmp eax,[ebx+eax*4]
        // Replace the impossible self-indexed compare with edi vs [ebx+eax*4].
        code[code.Count - 7] = 0x3B;
        code[code.Count - 6] = 0x3C;
        code[code.Count - 5] = 0x83;
        code.RemoveRange(code.Count - 4, 4);
        int playerFoundJump = AddConditionalJump(code, 0x84);
        code.Add(0x40);
        code.AddRange(new byte[] { 0x83, 0xF8, RequiredCompleteKingdomCount });
        int playerRepeatJump = AddConditionalJump(code, 0x8C);
        int noPlayerIndexJump = AddJump(code);

        int playerFoundOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xF1 });                              // esi=slot
        code.AddRange(new byte[] { 0x8B, 0xC8 });                              // ecx=player index
        code.AddRange(new byte[] { 0x8B, 0xC6 });                              // eax=slot
        code.AddRange(new byte[] { 0xC1, 0xE0, 0x03 });
        code.Add(0x05);
        code.AddRange(BitConverter.GetBytes(hostility.ToInt32()));
        code.AddRange(new byte[] { 0x83, 0xF9, 0x20 });
        int hostilityLowJump = AddConditionalJump(code, 0x82);
        code.AddRange(new byte[] { 0x83, 0xE9, 0x20, 0x83, 0xC0, 0x04 });
        int hostilityLowOffset = code.Count;
        code.AddRange(new byte[] { 0x0F, 0xA3, 0x08 });                        // bt [eax],ecx
        int provokedEnemyJump = AddConditionalJump(code, 0x82);               // jc enemy
        code.AddRange(new byte[] { 0x8B, 0xCE });                              // ecx=slot
        int raceAfterIndexJump = AddJump(code);

        int noPlayerIndexOffset = code.Count;
        // ECX is still the reserve slot when no list entry was found.
        int reserveRaceOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x14, 0xCD });                        // edx=family
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.AddRange(new byte[] { 0x8B, 0xCF });                              // ecx=player
        int reserveRaceCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int neutralFromReserveJump = AddConditionalJump(code, 0x85);
        stockJumps.Add(AddJump(code));

        int noReserveOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xCE, 0x8B, 0xD5 });                  // player=other,family=self
        int firstRaceCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int neutralFirstJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x8B, 0xCD, 0x8B, 0xD6 });                  // player=self,family=other
        int secondRaceCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int neutralSecondJump = AddConditionalJump(code, 0x85);

        // Herd hostility is personal too: only Undead and Shadow players see
        // animals as enemies; allied Human/Haroun/Drauga/Gauri players do not.
        code.AddRange(new byte[] { 0x3B, 0x6B, 0x08 });                        // self==herd
        int selfHerdJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x3B, 0x73, 0x08 });                        // other==herd
        int otherHerdJump = AddConditionalJump(code, 0x84);
        stockJumps.Add(AddJump(code));
        int selfHerdOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xCE });                              // player=other
        int herdCommonJump = AddJump(code);
        int otherHerdOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xCD });                              // player=self
        int herdCommonOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x53, 0x40 });                        // edx=undead family
        int undeadHerdCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int herdEnemyUndeadJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x8B, 0x53, 0x48 });                        // edx=shadow family
        int shadowHerdCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int herdEnemyShadowJump = AddConditionalJump(code, 0x85);
        stockJumps.Add(AddJump(code));

        int neutralOffset = code.Count;
        AddCounterIncrement(code, counters, CounterRaceNeutralOverrides);
        code.AddRange(new byte[] { 0xC7, 0x44, 0x24, 0x1C,
            RelationNeutral, 0, 0, 0 });
        int neutralReturnJump = AddJump(code);
        int herdEnemyOffset = code.Count;
        AddCounterIncrement(code, counters, CounterHerdEnemyOverrides);
        code.AddRange(new byte[] { 0xC7, 0x44, 0x24, 0x1C,
            RelationEnemy, 0, 0, 0 });
        int herdReturnJump = AddJump(code);
        int provokedEnemyOffset = code.Count;
        AddCounterIncrement(code, counters, CounterProvokedEnemyOverrides);
        code.AddRange(new byte[] { 0xC7, 0x44, 0x24, 0x1C,
            RelationEnemy, 0, 0, 0 });
        int provokedReturnJump = AddJump(code);

        int overrideReturnOffset = code.Count;
        code.Add(0x61);
        code.Add(0x9D);
        code.AddRange(new byte[] { 0xC2, 0x04, 0x00 });                        // ret 4
        int stockOffset = code.Count;
        code.Add(0x61);
        code.Add(0x9D);
        code.AddRange(original);
        int stockReturnJump = AddJump(code);

        int familyRaceHelperOffset = AppendFamilyRaceMatchHelper(
            code, strings, compareCalls);
        int compareOffset = AppendWideStringCompare(code);
        Dictionary<string, int> stringOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (StringReference reference in strings)
        {
            int stringOffset;
            if (!stringOffsets.TryGetValue(reference.Value, out stringOffset))
            {
                stringOffset = code.Count;
                stringOffsets.Add(reference.Value, stringOffset);
                code.AddRange(Encoding.Unicode.GetBytes(reference.Value + "\0"));
            }
            PatchInt32(code, reference.ImmediateOffset, Add(stub, stringOffset).ToInt32());
        }
        IntPtr helperAddress = Add(stub, familyRaceHelperOffset);
        IntPtr compareAddress = Add(stub, compareOffset);
        foreach (int call in compareCalls)
            PatchCall(code, stub, call, compareAddress);
        foreach (int call in new int[]
        {
            reserveRaceCall, firstRaceCall, secondRaceCall,
            undeadHerdCall, shadowHerdCall,
        })
            PatchCall(code, stub, call, helperAddress);

        IntPtr stockAddress = Add(stub, stockOffset);
        foreach (int jump in stockJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, stockAddress);
            else
                PatchConditionalJump(code, stub, jump, stockAddress);
        }
        PatchConditionalJump(code, stub, selfReserveJump, Add(stub, selfReserveOffset));
        PatchConditionalJump(code, stub, otherReserveJump, Add(stub, otherReserveOffset));
        PatchConditionalJump(code, stub, reserveRepeatJump, Add(stub, reserveLoopOffset));
        PatchJump(code, stub, noReserveJump, Add(stub, noReserveOffset));
        PatchJump(code, stub, reserveCommonJump, Add(stub, reserveCommonOffset));
        PatchConditionalJump(code, stub, playerFoundJump, Add(stub, playerFoundOffset));
        PatchConditionalJump(code, stub, playerRepeatJump, Add(stub, playerLoopOffset));
        PatchJump(code, stub, noPlayerIndexJump, Add(stub, noPlayerIndexOffset));
        PatchConditionalJump(code, stub, hostilityLowJump, Add(stub, hostilityLowOffset));
        PatchConditionalJump(code, stub, provokedEnemyJump, Add(stub, provokedEnemyOffset));
        PatchJump(code, stub, raceAfterIndexJump, Add(stub, reserveRaceOffset));
        PatchConditionalJump(code, stub, neutralFromReserveJump, Add(stub, neutralOffset));
        PatchConditionalJump(code, stub, neutralFirstJump, Add(stub, neutralOffset));
        PatchConditionalJump(code, stub, neutralSecondJump, Add(stub, neutralOffset));
        PatchConditionalJump(code, stub, selfHerdJump, Add(stub, selfHerdOffset));
        PatchConditionalJump(code, stub, otherHerdJump, Add(stub, otherHerdOffset));
        PatchJump(code, stub, herdCommonJump, Add(stub, herdCommonOffset));
        PatchConditionalJump(code, stub, herdEnemyUndeadJump, Add(stub, herdEnemyOffset));
        PatchConditionalJump(code, stub, herdEnemyShadowJump, Add(stub, herdEnemyOffset));
        PatchJump(code, stub, neutralReturnJump, Add(stub, overrideReturnOffset));
        PatchJump(code, stub, herdReturnJump, Add(stub, overrideReturnOffset));
        PatchJump(code, stub, provokedReturnJump, Add(stub, overrideReturnOffset));
        PatchJump(code, stub, stockReturnJump, Add(target, original.Length));
        return code.ToArray();
#endif
    }

#if HERD_RELATIONS_ONLY
    private static byte[] BuildHerdOnlyKingdomRelationStub(
        IntPtr imageBase,
        IntPtr target,
        byte[] original,
        IntPtr counters,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> stockJumps = new List<int>();
        List<int> compareCalls = new List<int>();
        List<StringReference> strings = new List<StringReference>();

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterPersonalRelationQueries);
        code.AddRange(new byte[] { 0x8B, 0x6C, 0x24, 0x18 });                  // ebp=saved ecx self
        code.AddRange(new byte[] { 0x8B, 0x74, 0x24, 0x28 });                  // esi=other argument
        code.AddRange(new byte[] { 0x85, 0xED });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x85, 0xF6 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0xEE });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0xA1);
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0,
            RequiredCompleteKingdomCount });
        stockJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });            // ebx=complete kingdoms
        code.AddRange(new byte[] { 0x85, 0xDB });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x3B, 0x6B, 0x08 });                        // self==herd
        int selfHerdJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x3B, 0x73, 0x08 });                        // other==herd
        int otherHerdJump = AddConditionalJump(code, 0x84);
        stockJumps.Add(AddJump(code));

        int selfHerdOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xCE });                              // player=other
        int herdCommonJump = AddJump(code);
        int otherHerdOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xCD });                              // player=self
        int herdCommonOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x53, 0x40 });                        // edx=undead family
        int undeadHerdCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int herdEnemyUndeadJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x8B, 0x53, 0x48 });                        // edx=shadow family
        int shadowHerdCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int herdEnemyShadowJump = AddConditionalJump(code, 0x85);
        stockJumps.Add(AddJump(code));

        int herdEnemyOffset = code.Count;
        AddCounterIncrement(code, counters, CounterHerdEnemyOverrides);
        code.AddRange(new byte[] { 0xC7, 0x44, 0x24, 0x1C,
            RelationEnemy, 0, 0, 0 });
        int herdReturnJump = AddJump(code);

        int overrideReturnOffset = code.Count;
        code.Add(0x61);
        code.Add(0x9D);
        code.AddRange(new byte[] { 0xC2, 0x04, 0x00 });                        // ret 4
        int stockOffset = code.Count;
        code.Add(0x61);
        code.Add(0x9D);
        code.AddRange(original);
        int stockReturnJump = AddJump(code);

        int helperOffset = AppendFamilyRaceMatchHelper(code, strings, compareCalls);
        int compareOffset = AppendWideStringCompare(code);
        Dictionary<string, int> stringOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (StringReference reference in strings)
        {
            int stringOffset;
            if (!stringOffsets.TryGetValue(reference.Value, out stringOffset))
            {
                stringOffset = code.Count;
                stringOffsets.Add(reference.Value, stringOffset);
                code.AddRange(Encoding.Unicode.GetBytes(reference.Value + "\0"));
            }
            PatchInt32(code, reference.ImmediateOffset, Add(stub, stringOffset).ToInt32());
        }
        IntPtr helperAddress = Add(stub, helperOffset);
        IntPtr compareAddress = Add(stub, compareOffset);
        foreach (int call in compareCalls)
            PatchCall(code, stub, call, compareAddress);
        PatchCall(code, stub, undeadHerdCall, helperAddress);
        PatchCall(code, stub, shadowHerdCall, helperAddress);

        IntPtr stockAddress = Add(stub, stockOffset);
        foreach (int jump in stockJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, stockAddress);
            else
                PatchConditionalJump(code, stub, jump, stockAddress);
        }
        PatchConditionalJump(code, stub, selfHerdJump, Add(stub, selfHerdOffset));
        PatchConditionalJump(code, stub, otherHerdJump, Add(stub, otherHerdOffset));
        PatchJump(code, stub, herdCommonJump, Add(stub, herdCommonOffset));
        PatchConditionalJump(code, stub, herdEnemyUndeadJump, Add(stub, herdEnemyOffset));
        PatchConditionalJump(code, stub, herdEnemyShadowJump, Add(stub, herdEnemyOffset));
        PatchJump(code, stub, herdReturnJump, Add(stub, overrideReturnOffset));
        PatchJump(code, stub, stockReturnJump, Add(target, original.Length));
        return code.ToArray();
    }
#endif

    // ECX=playable kingdom, EDX=family kingdom, EBX=complete kingdom array.
    private static int AppendFamilyRaceMatchHelper(
        List<byte> code,
        List<StringReference> strings,
        List<int> compareCalls)
    {
        int offset = code.Count;
        List<int> matchJumps = new List<int>();
        List<int> failJumps = new List<int>();
        code.AddRange(new byte[] { 0x53, 0x56, 0x57, 0x55 });                  // preserve ebx,esi,edi,ebp
        code.AddRange(new byte[] { 0x8B, 0xE9, 0x8B, 0xF2 });                  // ebp=player,esi=family
        code.AddRange(new byte[] { 0x85, 0xED });
        failJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x85, 0xF6 });
        failJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x95, 0x40, 0x02, 0, 0 });           // nation
        code.AddRange(new byte[] { 0x85, 0xD2 });
        failJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x52, 0x08 });                        // race IDS
        code.AddRange(new byte[] { 0x85, 0xD2 });
        failJumps.Add(AddConditionalJump(code, 0x84));

        int[] familyIndices = { 19, 20, 16, 18, 21, 22 };
        string[] lower = { "human", "haroun", "undead", "shadow", "drauga", "gauri" };
        string[] title = { "Human", "Haroun", "Undead", "Shadow", "Drauga", "Gauri" };
        List<int> nextFamilyJumps = new List<int>();
        List<int> nextFamilyOffsets = new List<int>();
        for (int i = 0; i < familyIndices.Length; ++i)
        {
            if (nextFamilyJumps.Count > nextFamilyOffsets.Count)
                nextFamilyOffsets.Add(code.Count);
            code.AddRange(new byte[] { 0x3B, 0x73, (byte)(familyIndices[i] * 4) });
            nextFamilyJumps.Add(AddConditionalJump(code, 0x85));
            int lowerImmediate = AddMovEdiString(code);
            strings.Add(new StringReference(lowerImmediate, lower[i]));
            compareCalls.Add(AddCall(code));
            code.AddRange(new byte[] { 0x85, 0xC0 });
            matchJumps.Add(AddConditionalJump(code, 0x85));
            int titleImmediate = AddMovEdiString(code);
            strings.Add(new StringReference(titleImmediate, title[i]));
            compareCalls.Add(AddCall(code));
            code.AddRange(new byte[] { 0x85, 0xC0 });
            matchJumps.Add(AddConditionalJump(code, 0x85));
            failJumps.Add(AddJump(code));
        }
        if (nextFamilyJumps.Count > nextFamilyOffsets.Count)
            nextFamilyOffsets.Add(code.Count);
        int failOffset = code.Count;
        code.AddRange(new byte[] { 0x33, 0xC0 });                              // false
        int finishJump = AddJump(code);
        int matchOffset = code.Count;
        code.AddRange(new byte[] { 0xB8, 1, 0, 0, 0 });                       // true
        int finishOffset = code.Count;
        code.AddRange(new byte[] { 0x5D, 0x5F, 0x5E, 0x5B, 0xC3 });
        for (int i = 0; i < nextFamilyJumps.Count; ++i)
            PatchConditionalJump(
                code, IntPtr.Zero, nextFamilyJumps[i], new IntPtr(nextFamilyOffsets[i]));
        foreach (int jump in failJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, IntPtr.Zero, jump, new IntPtr(failOffset));
            else
                PatchConditionalJump(code, IntPtr.Zero, jump, new IntPtr(failOffset));
        }
        foreach (int jump in matchJumps)
            PatchConditionalJump(code, IntPtr.Zero, jump, new IntPtr(matchOffset));
        PatchJump(code, IntPtr.Zero, finishJump, new IntPtr(finishOffset));
        return offset;
    }

    private static void AddProvokedFamilyRaceBranch(
        List<byte> code,
        int familyArrayOffset,
        string lowerRace,
        string titleRace,
        List<StringReference> strings,
        List<int> compareCalls,
        List<int> raceMatchJumps,
        List<int> nextFamilyJumps,
        List<int> nextFamilyOffsets)
    {
        if (nextFamilyJumps.Count > 0 &&
            nextFamilyOffsets.Count < nextFamilyJumps.Count)
            nextFamilyOffsets.Add(code.Count);
        code.AddRange(new byte[] { 0x3B, 0x6B, (byte)familyArrayOffset });       // cmp ebp,[ebx+family*4]
        nextFamilyJumps.Add(AddConditionalJump(code, 0x85));                  // jne next family
        raceMatchJumps.Add(AddRaceCompare(
            code, lowerRace, strings, compareCalls));
        raceMatchJumps.Add(AddRaceCompare(
            code, titleRace, strings, compareCalls));
        // Family matched but the player's race did not.
        int skipPlayer = AddJump(code);
        // Use the same list; this unconditional jump is patched by the caller.
        // It is encoded into nextPlayerJumps after all family branches.
        // Store it as a negative marker is unnecessary, so append a tiny local
        // bridge that is filled when the following family begins.
        int bridgeOffset = code.Count;
        PatchJump(code, IntPtr.Zero, skipPlayer, new IntPtr(bridgeOffset));
    }

    private static void InstallUiNeutralFamilyAttackGateHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr target = Add(imageBase, UiAttackTargetGateRva);
        byte[] expected =
        {
            0x8B, 0x10,                         // mov edx,[eax]
            0x8B, 0xC8,                         // mov ecx,eax
            0x57,                               // push edi
            0xFF, 0x92, 0x84, 0x00, 0x00, 0x00 // call [edx+84]
        };
        byte[] actual = ReadBytes(process, target, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала проверка цели команды Атака 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 2048,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(UI neutral-family attack gate)");
        byte[] code = BuildUiNeutralFamilyAttackGateStub(
            imageBase, target, expected, counters, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(UI neutral-family attack gate)");

        byte[] detour = Enumerable.Repeat((byte)0x90, expected.Length).ToArray();
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        WriteCodePatch(
            process,
            target,
            detour,
            "UI neutral-family Attack target gate");
        AppendLog(
            logPath,
            "UI NEUTRAL-FAMILY ATTACK GATE PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " policy=stock-first-family-only.");
    }

    private static byte[] BuildUiNeutralFamilyAttackGateStub(
        IntPtr imageBase,
        IntPtr target,
        byte[] original,
        IntPtr counters,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> notEligibleJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        AddCounterIncrement(code, counters, CounterUiGateEntries);
        code.Add(0x50);                                                        // preserve target actor
        code.AddRange(original);                                               // stock relation gate
        code.AddRange(new byte[] { 0x84, 0xC0 });                              // test al,al
        int stockAcceptedJump = AddConditionalJump(code, 0x85);               // jne finish

        AddCounterIncrement(code, counters, CounterUiGateStockRejected);
        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        code.AddRange(new byte[] { 0x8B, 0x44, 0x24, 0x24 });                  // mov eax,saved target
        code.AddRange(new byte[] { 0x85, 0xC0 });
        notEligibleJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x90, 0xE8, 0x00, 0x00, 0x00 });     // mov edx,[target+E8]
        code.AddRange(new byte[] { 0x85, 0xD2 });
        notEligibleJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        notEligibleJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0x00, 0x00, 0x17 }); // count >=23
        notEligibleJumps.Add(AddConditionalJump(code, 0x82));                 // jb not eligible
        code.AddRange(new byte[] { 0x8B, 0x88, 0x50, 0x01, 0x00, 0x00 });     // mov ecx,kingdoms
        code.AddRange(new byte[] { 0x85, 0xC9 });
        notEligibleJumps.Add(AddConditionalJump(code, 0x84));
        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x51, (byte)(familyIndex * 4) }); // cmp edx,[ecx+family]
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        notEligibleJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterUiAttackOverrides);
        code.AddRange(new byte[]
        {
            0xC7, 0x44, 0x24, 0x1C, 0x01, 0x00, 0x00, 0x00,                 // saved EAX=1
        });

        int restoreOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int finishOffset = code.Count;
        code.AddRange(new byte[] { 0x83, 0xC4, 0x04 });                       // discard saved target
        int returnJump = AddJump(code);

        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        foreach (int jump in notEligibleJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, Add(stub, restoreOffset));
            else
                PatchConditionalJump(code, stub, jump, Add(stub, restoreOffset));
        }
        PatchConditionalJump(code, stub, stockAcceptedJump, Add(stub, finishOffset));
        PatchJump(code, stub, returnJump, Add(target, original.Length));
        return code.ToArray();
    }

    private static void InstallUiNeutralFamilyCommandValidationHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr target = Add(imageBase, UiAttackCommandValidationRva);
        byte[] expected =
        {
            0x85, 0xC0,                               // test eax,eax
            0x0F, 0x84, 0x18, 0x01, 0x00, 0x00,       // je valid command path
        };
        byte[] actual = ReadBytes(process, target, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала проверка результата команды Атака 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 2048,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(UI Attack command validation)");
        byte[] code = BuildUiNeutralFamilyCommandValidationStub(
            imageBase, counters, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(UI Attack command validation)");

        byte[] detour = Enumerable.Repeat((byte)0x90, expected.Length).ToArray();
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        WriteCodePatch(
            process,
            target,
            detour,
            "UI neutral-family Attack command validation");
        AppendLog(
            logPath,
            "UI ATTACK COMMAND VALIDATION PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " policy=error-only-command-kind-4-family-only.");
    }

    private static byte[] BuildUiNeutralFamilyCommandValidationStub(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> restoreJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        AddCounterIncrement(code, counters, CounterUiCommandValidationEntries);
        code.Add(0xA3);                                                        // mov [last error],eax
        code.AddRange(BitConverter.GetBytes(
            Add(counters, CounterLastUiCommandError * 4).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });                              // stock test eax,eax
        int alreadyValidJump = AddConditionalJump(code, 0x84);                // je valid path
        AddCounterIncrement(code, counters, CounterUiCommandValidationErrors);
        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad

        code.AddRange(new byte[] { 0x8B, 0x5C, 0x24, 0x10 });                  // mov ebx,saved UI state
        code.AddRange(new byte[] { 0x85, 0xDB });
        restoreJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x5B, 0x04 });                        // mov ebx,[state+4] command data
        code.AddRange(new byte[] { 0x85, 0xDB });
        restoreJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x4B, 0x0C });                        // mov ecx,[command+0C] kind
        code.AddRange(new byte[] { 0x89, 0x0D });                              // mov [last kind],ecx
        code.AddRange(BitConverter.GetBytes(
            Add(counters, CounterLastUiCommandKind * 4).ToInt32()));
        code.AddRange(new byte[] { 0x83, 0xF9, 0x04 });                        // command kind 4 (Attack)
        restoreJumps.Add(AddConditionalJump(code, 0x85));
        AddCounterIncrement(code, counters, CounterUiCommandKind4Errors);

        code.AddRange(new byte[] { 0x8B, 0x4C, 0x24, 0x04 });                  // mov ecx,saved target entry
        code.AddRange(new byte[] { 0x85, 0xC9 });
        restoreJumps.Add(AddConditionalJump(code, 0x84));
        int resolveActorCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        restoreJumps.Add(AddConditionalJump(code, 0x84));
        AddCounterIncrement(code, counters, CounterUiCommandResolvedActors);
        code.AddRange(new byte[] { 0x8B, 0x90, 0xE8, 0x00, 0x00, 0x00 });     // mov edx,[target+E8]
        code.AddRange(new byte[] { 0x85, 0xD2 });
        restoreJumps.Add(AddConditionalJump(code, 0x84));

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        restoreJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0x00, 0x00, 0x17 }); // count >=23
        restoreJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x88, 0x50, 0x01, 0x00, 0x00 });     // mov ecx,kingdoms
        code.AddRange(new byte[] { 0x85, 0xC9 });
        restoreJumps.Add(AddConditionalJump(code, 0x84));
        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x51, (byte)(familyIndex * 4) }); // cmp edx,[ecx+family]
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        restoreJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterUiCommandFamilyTargets);
        AddCounterIncrement(code, counters, CounterUiCommandValidationOverrides);
        code.AddRange(new byte[]
        {
            0xC7, 0x44, 0x24, 0x1C, 0x00, 0x00, 0x00, 0x00,                 // saved EAX=valid
        });

        int restoreOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        code.AddRange(new byte[] { 0x85, 0xC0 });                              // final result
        int validJump = AddConditionalJump(code, 0x84);
        int invalidJump = AddJump(code);

        PatchConditionalJump(
            code, stub, alreadyValidJump, Add(imageBase, UiAttackCommandValidRva));
        PatchCall(
            code, stub, resolveActorCall, Add(imageBase, UiResolveTargetActorRva));
        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        foreach (int jump in restoreJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, Add(stub, restoreOffset));
            else
                PatchConditionalJump(code, stub, jump, Add(stub, restoreOffset));
        }
        PatchConditionalJump(
            code, stub, validJump, Add(imageBase, UiAttackCommandValidRva));
        PatchJump(
            code, stub, invalidJump, Add(imageBase, UiAttackCommandInvalidRva));
        return code.ToArray();
    }

    private static void InstallUiActorCommandNeutralFamilyTargetHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        IntPtr directProvocationWorker,
        string logPath)
    {
        IntPtr target = Add(imageBase, UiActorCommandTargetGateRva);
        byte[] expected =
        {
            0x0F, 0x84, 0x25, 0x01, 0x00, 0x00, // je stock valid target
        };
        byte[] actual = ReadBytes(process, target, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала проверка цели ActorCommand kill 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 2048,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(ActorCommand kill target gate)");
        byte[] code = BuildUiActorCommandNeutralFamilyTargetStub(
            imageBase, counters, directProvocationWorker, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(ActorCommand kill target gate)");

        byte[] detour = new byte[expected.Length];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        detour[5] = 0x90;
        WriteCodePatch(
            process,
            target,
            detour,
            "ActorCommand kill neutral-family target gate");
        AppendLog(
            logPath,
            "ACTORCOMMAND KILL NEUTRAL-FAMILY TARGET GATE PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " policy=stock-first-family-only.");
    }

    private static byte[] BuildUiActorCommandNeutralFamilyTargetStub(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr directProvocationWorker,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> invalidJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();
        List<int> skipProvocationJumps = new List<int>();

        // Preserve the stock ZF produced by `test dl,dl` while recording that
        // this is the correct target gate used by ActorCommand kill.
        code.Add(0x9C);                                                        // pushfd
        AddCounterIncrement(code, counters, CounterUiActorTargetGateEntries);
        code.Add(0x9D);                                                        // popfd
        int stockAcceptedJump = AddConditionalJump(code, 0x84);               // je stock valid

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterUiActorTargetStockRejected);
        code.AddRange(new byte[] { 0x85, 0xFF });                              // target actor in EDI
        invalidJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x97, 0xE8, 0x00, 0x00, 0x00 });     // owner kingdom
        code.AddRange(new byte[] { 0x85, 0xD2 });
        invalidJumps.Add(AddConditionalJump(code, 0x84));

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        invalidJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0x00, 0x00, 0x17 }); // count >=23
        invalidJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x88, 0x50, 0x01, 0x00, 0x00 });     // kingdoms
        code.AddRange(new byte[] { 0x85, 0xC9 });
        invalidJumps.Add(AddConditionalJump(code, 0x84));
        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x51, (byte)(familyIndex * 4) }); // cmp owner,[family]
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        invalidJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterUiActorTargetFamilyOverrides);
        AddCounterIncrement(code, counters, CounterUiImmediateProvocationAttempts);
        // The stock handler already resolved the local player's actual
        // kingdom and keeps it in [ebp-14]. This is deliberately not a
        // selected/target actor owner or the shared multiplayer team parent.
        code.AddRange(new byte[] { 0x8B, 0x45, 0xEC });                        // player kingdom [ebp-14]
        code.AddRange(new byte[] { 0x85, 0xC0 });
        skipProvocationJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0x57);                                                        // target actor
        code.Add(0x50);                                                        // attacking kingdom
        int directProvocationCall = AddCall(code);

        int provocationCompleteOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int eligibleJump = AddJump(code);

        int invalidOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int invalidJump = AddJump(code);

        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        foreach (int jump in skipProvocationJumps)
            PatchConditionalJump(
                code, stub, jump, Add(stub, provocationCompleteOffset));
        foreach (int jump in invalidJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, Add(stub, invalidOffset));
            else
                PatchConditionalJump(code, stub, jump, Add(stub, invalidOffset));
        }
        PatchConditionalJump(
            code, stub, stockAcceptedJump, Add(imageBase, UiActorCommandTargetValidRva));
        PatchCall(code, stub, directProvocationCall, directProvocationWorker);
        PatchJump(
            code, stub, eligibleJump, Add(imageBase, UiActorCommandTargetValidRva));
        PatchJump(
            code, stub, invalidJump, Add(imageBase, UiActorCommandTargetInvalidRva));
        return code.ToArray();
    }

    private static void InstallActorCommandDispatchNeutralFamilyValidationHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr target = Add(imageBase, ActorCommandDispatchValidationRva);
        byte[] expected =
        {
            0x84, 0xC0,                               // test al,al
            0x0F, 0x85, 0x98, 0x01, 0x00, 0x00,       // jne stock success
        };
        byte[] actual = ReadBytes(process, target, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала внутренняя проверка отправки ActorCommand 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 2048,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(ActorCommand dispatch validation)");
        byte[] code = BuildActorCommandDispatchNeutralFamilyValidationStub(
            imageBase,
            counters,
            stub,
            ActorCommandDispatchValidationSuccessRva,
            ActorCommandDispatchValidationFailureRva);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(ActorCommand dispatch validation)");

        byte[] detour = new byte[expected.Length];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        for (int index = 5; index < detour.Length; ++index)
            detour[index] = 0x90;
        WriteCodePatch(
            process,
            target,
            detour,
            "ActorCommand neutral-family dispatch validation");
        AppendLog(
            logPath,
            "ACTORCOMMAND NEUTRAL-FAMILY DISPATCH VALIDATION PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " policy=stock-first-family-only.");
    }

    private static byte[] BuildActorCommandDispatchNeutralFamilyValidationStub(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr stub,
        int successRva,
        int failureRva)
    {
        List<byte> code = new List<byte>();
        List<int> rejectedJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterUiCommandValidationEntries);
        code.AddRange(new byte[] { 0x8B, 0x44, 0x24, 0x1C });                  // saved EAX
        code.AddRange(new byte[] { 0x84, 0xC0 });                              // stock result
        int stockAcceptedJump = AddConditionalJump(code, 0x85);

        AddCounterIncrement(code, counters, CounterUiCommandValidationErrors);
        code.AddRange(new byte[] { 0x8B, 0x74, 0x24, 0x04 });                  // saved ESI: &command
        code.AddRange(new byte[] { 0x85, 0xF6 });
        rejectedJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x36 });                              // command
        code.AddRange(new byte[] { 0x85, 0xF6 });
        rejectedJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0xF6, 0x46, 0x0C, 0x01 });                  // target-actor flag
        rejectedJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x8B, 0x46, 0x10 });                        // target actor ID
        code.Add(0x50);
        code.AddRange(new byte[] { 0x8B, 0x0D });
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, ActorLookupManagerRva).ToInt32()));
        int actorLookupCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        rejectedJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0xF8 });                              // target actor
        AddCounterIncrement(code, counters, CounterUiCommandResolvedActors);
        code.AddRange(new byte[] { 0x8B, 0x97, 0xE8, 0x00, 0x00, 0x00 });     // owner kingdom
        code.AddRange(new byte[] { 0x85, 0xD2 });
        rejectedJumps.Add(AddConditionalJump(code, 0x84));

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        rejectedJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0x00, 0x00, 0x17 });
        rejectedJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x88, 0x50, 0x01, 0x00, 0x00 });     // kingdoms
        code.AddRange(new byte[] { 0x85, 0xC9 });
        rejectedJumps.Add(AddConditionalJump(code, 0x84));
        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x51, (byte)(familyIndex * 4) });
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        rejectedJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterUiCommandFamilyTargets);
        AddCounterIncrement(code, counters, CounterUiCommandValidationOverrides);

        int acceptedOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int acceptedJump = AddJump(code);

        int rejectedOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int rejectedJump = AddJump(code);

        PatchConditionalJump(code, stub, stockAcceptedJump, Add(stub, acceptedOffset));
        PatchCall(code, stub, actorLookupCall, Add(imageBase, ActorLookupByIdRva));
        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        foreach (int jump in rejectedJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, Add(stub, rejectedOffset));
            else
                PatchConditionalJump(code, stub, jump, Add(stub, rejectedOffset));
        }
        PatchJump(
            code, stub, acceptedJump,
            Add(imageBase, successRva));
        PatchJump(
            code, stub, rejectedJump,
            Add(imageBase, failureRva));
        return code.ToArray();
    }

    private static void InstallActorCommandDispatchFallbackNeutralFamilyValidationHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr target = Add(imageBase, ActorCommandDispatchFallbackValidationRva);
        byte[] expected =
        {
            0x84, 0xC0,                               // test al,al
            0x0F, 0x85, 0x0C, 0x01, 0x00, 0x00,       // jne stock success
        };
        byte[] actual = ReadBytes(process, target, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала альтернативная проверка отправки ActorCommand 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 2048,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(ActorCommand fallback validation)");
        byte[] code = BuildActorCommandDispatchNeutralFamilyValidationStub(
            imageBase,
            counters,
            stub,
            ActorCommandDispatchFallbackSuccessRva,
            ActorCommandDispatchFallbackFailureRva);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(ActorCommand fallback validation)");

        byte[] detour = new byte[expected.Length];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        for (int index = 5; index < detour.Length; ++index)
            detour[index] = 0x90;
        WriteCodePatch(
            process,
            target,
            detour,
            "ActorCommand neutral-family fallback validation");
        AppendLog(
            logPath,
            "ACTORCOMMAND NEUTRAL-FAMILY FALLBACK VALIDATION PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " policy=stock-first-family-only.");
    }

    private static void InstallMinimapNeutralFamilyVisibilityHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        string logPath)
    {
        IntPtr target = Add(imageBase, MinimapNeutralFamilyGateRva);
        byte[] expected =
        {
            0x85, 0xF6,                         // test esi,esi
            0x74, 0x0D,                         // je stock false
            0x83, 0xBE, 0x98, 0x01, 0x00, 0x00, 0x00, // cmp [esi+198],0
            0x75, 0x04,                         // jne stock false
        };
        byte[] actual = ReadBytes(process, target, expected.Length);
        if (!actual.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпала финальная проверка нейтрального значка миникарты 1.3.68: " +
                BitConverter.ToString(actual));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 2048,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(minimap neutral-family visibility)");
        byte[] code = BuildMinimapNeutralFamilyVisibilityStub(
            imageBase, counters, stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(minimap neutral-family visibility)");

        byte[] detour = Enumerable.Repeat((byte)0x90, expected.Length).ToArray();
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        WriteCodePatch(
            process,
            target,
            detour,
            "minimap neutral-family visibility gate");
        AppendLog(
            logPath,
            "MINIMAP NEUTRAL-FAMILY VISIBILITY PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " policy=six-families+12-provoked-reserves-stock-neutral-colour.");
    }

    private static byte[] BuildMinimapNeutralFamilyVisibilityStub(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> stockJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        code.AddRange(new byte[] { 0x85, 0xF6 });                              // test esi,esi
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0x00, 0x00,
            ReserveFirstKingdomIndex + ReserveKingdomCount });                // count >=35
        stockJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x90, 0x50, 0x01, 0x00, 0x00 });     // mov edx,kingdoms
        code.AddRange(new byte[] { 0x85, 0xD2 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x72, (byte)(familyIndex * 4) }); // cmp esi,[edx+family]
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        code.AddRange(new byte[] { 0x33, 0xC9 });                              // reserve slot=0
        int reserveLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x3B, 0x74, 0x8A,
            ReserveFirstKingdomIndex * 4 });                                  // cmp esi,[edx+ecx*4+reserve]
        eligibleJumps.Add(AddConditionalJump(code, 0x84));
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int reserveRepeatJump = AddConditionalJump(code, 0x8C);
        stockJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterMinimapNeutralFamilyIcons);
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        int successJump = AddJump(code);

        int stockOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        code.AddRange(new byte[] { 0x85, 0xF6 });                              // stock test esi,esi
        int falseNullJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x83, 0xBE, 0x98, 0x01, 0x00, 0x00, 0x00 }); // cmp [esi+198],0
        int falseIndependentJump = AddConditionalJump(code, 0x85);
        int blendJump = AddJump(code);

        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        PatchConditionalJump(code, stub, reserveRepeatJump, Add(stub, reserveLoopOffset));
        foreach (int jump in stockJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, Add(stub, stockOffset));
            else
                PatchConditionalJump(code, stub, jump, Add(stub, stockOffset));
        }
        PatchJump(
            code, stub, successJump, Add(imageBase, MinimapNeutralFamilySuccessRva));
        PatchConditionalJump(
            code, stub, falseNullJump, Add(imageBase, MinimapNeutralFamilyFalseRva));
        PatchConditionalJump(
            code, stub, falseIndependentJump, Add(imageBase, MinimapNeutralFamilyFalseRva));
        PatchJump(
            code, stub, blendJump, Add(imageBase, MinimapNeutralFamilyBlendRva));
        return code.ToArray();
    }

    private static IntPtr CreateDirectProvocationWorker(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        IntPtr state,
        IntPtr hostilityWorker,
        IntPtr relationWorker,
        string logPath)
    {
        IntPtr worker = VirtualAllocEx(
            process, IntPtr.Zero, 4096,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (worker == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(direct provocation worker)");
        byte[] code = BuildDirectProvocationWorker(
            imageBase,
            counters,
            state,
            hostilityWorker,
            relationWorker,
            worker);
        WriteBytes(process, worker, code);
        if (!FlushInstructionCache(process, worker, code.Length))
            ThrowWin32("FlushInstructionCache(direct provocation worker)");
        AppendLog(
            logPath,
            "DIRECT PROVOCATION WORKER CREATED address=0x" +
            worker.ToInt64().ToString("X8") +
            " trigger=accepted-neutral-family-ActorCommand-before-stock-dispatch.");
        return worker;
    }

    // Custom stdcall helper: (attacking playable kingdom, target actor).
    private static byte[] BuildDirectProvocationWorker(
        IntPtr imageBase,
        IntPtr counters,
        IntPtr state,
        IntPtr hostilityWorker,
        IntPtr relationWorker,
        IntPtr worker)
    {
        List<byte> code = new List<byte>();
        List<int> doneJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        code.Add(0x55);                                                        // push ebp
        code.AddRange(new byte[] { 0x8B, 0xEC });                              // mov ebp,esp
        code.Add(0x60);                                                        // pushad
        code.AddRange(new byte[] { 0x8B, 0x75, 0x08 });                        // attacking kingdom
        code.AddRange(new byte[] { 0x8B, 0x7D, 0x0C });                        // target actor
        code.AddRange(new byte[] { 0x85, 0xF6 });
        doneJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x85, 0xFF });
        doneJumps.Add(AddConditionalJump(code, 0x84));

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        doneJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0,
            RequiredCompleteKingdomCount });
        doneJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });           // kingdoms
        code.AddRange(new byte[] { 0x85, 0xDB });
        doneJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x97, 0xE8, 0, 0, 0 });              // target owner
        code.AddRange(new byte[] { 0x85, 0xD2 });
        doneJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x33, 0xC9 });                              // reserve scan
        int existingLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x3B, 0x54, 0x8B,
            ReserveFirstKingdomIndex * 4 });
        int existingJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int existingLoopJump = AddConditionalJump(code, 0x8C);

        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x53, (byte)(familyIndex * 4) });
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        doneJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterEligibleNeutralTargets);
        code.AddRange(new byte[] { 0x33, 0xC9 });
        int freeLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x83, 0x3C, 0xCD });
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.Add(0x00);
        int freeFoundJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int freeLoopJump = AddConditionalJump(code, 0x8C);
        AddCounterIncrement(code, counters, CounterProvokedSlotsExhausted);
        doneJumps.Add(AddJump(code));

        int freeFoundOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xF1 });                              // slot
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });                                  // reserve kingdom
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int missingReserveJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x89, 0x3C, 0xF5 });
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.AddRange(new byte[] { 0x89, 0x14, 0xF5 });
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.Add(0x52);                                                        // family
        code.Add(0x50);                                                        // reserve
        int relationCall = AddCall(code);
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });
        code.Add(0x50);
        code.AddRange(new byte[] { 0x8B, 0xCF });                              // target actor
        int setKingdomCall = AddCall(code);
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });
        code.AddRange(new byte[] { 0x39, 0x87, 0xE8, 0, 0, 0 });
        int setKingdomFailedJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x8B, 0x55, 0x08 });                        // attacking kingdom
        code.Add(0x52);
        code.Add(0x56);
        int newHostilityCall = AddCall(code);
        AddCounterIncrement(code, counters, CounterProvokedAssignments);
        AddCounterIncrement(code, counters, CounterUiImmediateProvocationAssignments);
        int assignmentDoneJump = AddJump(code);

        int setKingdomFailedOffset = code.Count;
        code.AddRange(new byte[] { 0xC7, 0x04, 0xF5 });
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.AddRange(new byte[] { 0, 0, 0, 0 });
        code.AddRange(new byte[] { 0xC7, 0x04, 0xF5 });
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.AddRange(new byte[] { 0, 0, 0, 0 });
        AddCounterIncrement(code, counters, CounterSetKingdomFailures);
        int failureDoneJump = AddJump(code);

        int existingOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0x55, 0x08 });                        // attacking kingdom
        code.Add(0x52);
        code.Add(0x51);                                                        // existing slot
        int existingHostilityCall = AddCall(code);
        AddCounterIncrement(code, counters, CounterExistingProvokedOrders);
        AddCounterIncrement(code, counters, CounterUiImmediateProvocationAssignments);
        int existingDoneJump = AddJump(code);

        int doneOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x5D);                                                        // pop ebp
        code.AddRange(new byte[] { 0xC2, 0x08, 0x00 });                        // ret 8

        foreach (int jump in doneJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, worker, jump, Add(worker, doneOffset));
            else
                PatchConditionalJump(code, worker, jump, Add(worker, doneOffset));
        }
        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, worker, jump, Add(worker, eligibleOffset));
        PatchConditionalJump(code, worker, existingJump, Add(worker, existingOffset));
        PatchConditionalJump(code, worker, existingLoopJump, Add(worker, existingLoopOffset));
        PatchConditionalJump(code, worker, freeFoundJump, Add(worker, freeFoundOffset));
        PatchConditionalJump(code, worker, freeLoopJump, Add(worker, freeLoopOffset));
        PatchConditionalJump(code, worker, missingReserveJump, Add(worker, setKingdomFailedOffset));
        PatchCall(code, worker, relationCall, relationWorker);
        PatchCall(code, worker, setKingdomCall, Add(imageBase, ActorSetKingdomRva));
        PatchConditionalJump(code, worker, setKingdomFailedJump, Add(worker, setKingdomFailedOffset));
        PatchCall(code, worker, newHostilityCall, hostilityWorker);
        PatchJump(code, worker, assignmentDoneJump, Add(worker, doneOffset));
        PatchJump(code, worker, failureDoneJump, Add(worker, doneOffset));
        PatchCall(code, worker, existingHostilityCall, hostilityWorker);
        PatchJump(code, worker, existingDoneJump, Add(worker, doneOffset));
        return code.ToArray();
    }

    private static void InstallExplicitAttackProvocationHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        IntPtr state,
        IntPtr hostilityWorker,
        IntPtr relationWorker,
        string logPath)
    {
        IntPtr target = Add(imageBase, AttackOrderHookRva);
        byte[] original = ReadBytes(process, target, 5);
        int expectedContext = Add(imageBase, AttackOrderContextRva).ToInt32();
        if (original[0] != 0xB9 ||
            BitConverter.ToInt32(original, 1) != expectedContext)
            throw new InvalidOperationException(
                "Не совпал обработчик явного приказа Атака: " +
                BitConverter.ToString(original));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 8192,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(explicit attack provocation stub)");
        byte[] code = BuildExplicitAttackProvocationStub(
            imageBase,
            target,
            original,
            counters,
            state,
            hostilityWorker,
            relationWorker,
            stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(explicit attack provocation stub)");

        byte[] detour = new byte[5];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        WriteCodePatch(
            process,
            target,
            detour,
            "explicit Attack neutral-site provocation detour");
        AppendLog(
            logPath,
            "EXPLICIT ATTACK PROVOCATION HOOK PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " state=0x" + state.ToInt64().ToString("X8") +
            " reserves=" + ReserveKingdomCount +
            " trigger=TellActorAttackOrder ordinary-right-click=unchanged.");
    }

    private static byte[] BuildExplicitAttackProvocationStub(
        IntPtr imageBase,
        IntPtr target,
        byte[] original,
        IntPtr counters,
        IntPtr state,
        IntPtr hostilityWorker,
        IntPtr relationWorker,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> stockJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterAttackOrders);
        code.AddRange(new byte[] { 0x8B, 0x6C, 0x24, 0x04 });                  // mov ebp,saved ESI order
        code.AddRange(new byte[] { 0x85, 0xED });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x75, 0x04 });                        // mov esi,[order+4] source
        code.AddRange(new byte[] { 0x8B, 0x7D, 0x08 });                        // mov edi,[order+8] target
        code.AddRange(new byte[] { 0x85, 0xF6 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x85, 0xFF });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0,
            RequiredCompleteKingdomCount });
        stockJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });           // mov ebx,complete kingdoms
        code.AddRange(new byte[] { 0x85, 0xDB });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0xAF, 0xE8, 0, 0, 0 });              // mov ebp,[target+E8]
        code.AddRange(new byte[] { 0x85, 0xED });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x96, 0xE8, 0, 0, 0 });              // mov edx,[source+E8]
        code.AddRange(new byte[] { 0x85, 0xD2 });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x33, 0xC9 });                              // xor ecx,ecx
        int existingLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x3B, 0x6C, 0x8B,
            ReserveFirstKingdomIndex * 4 });                                  // cmp ebp,[ebx+ecx*4+reserve base]
        int existingJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);                                                        // inc ecx
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });         // cmp ecx,12
        int existingLoopJump = AddConditionalJump(code, 0x8C);                // jl loop

        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x6B, (byte)(familyIndex * 4) }); // cmp ebp,[ebx+family]
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        stockJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterEligibleNeutralTargets);
        code.AddRange(new byte[] { 0x33, 0xC9 });                              // xor ecx,ecx
        int freeLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x83, 0x3C, 0xCD });                        // cmp dword [ecx*8+state],0
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.Add(0x00);
        int freeFoundJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int freeLoopJump = AddConditionalJump(code, 0x8C);
        AddCounterIncrement(code, counters, CounterProvokedSlotsExhausted);
        int exhaustedStockJump = AddJump(code);

        int freeFoundOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xF1 });                              // mov esi,ecx slot
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });                                  // mov eax,[ebx+esi*4+reserve base]
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int missingReserveJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x89, 0x3C, 0xF5 });                        // state[slot].target=edi
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.AddRange(new byte[] { 0x89, 0x2C, 0xF5 });                        // state[slot].family=ebp
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.Add(0x55);                                                        // push family
        code.Add(0x50);                                                        // push reserve
        int provokedRelationCall = AddCall(code);

        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });                                  // reload reserve
        code.Add(0x50);
        code.AddRange(new byte[] { 0x8B, 0xCF });                              // mov ecx,edi target actor
        int setKingdomCall = AddCall(code);
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });
        code.AddRange(new byte[] { 0x39, 0x87, 0xE8, 0, 0, 0 });              // cmp [target+E8],eax
        int setKingdomFailedJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x8B, 0x54, 0x24, 0x04 });                  // reload order
        code.AddRange(new byte[] { 0x8B, 0x52, 0x04 });                        // source actor
        code.AddRange(new byte[] { 0x8B, 0x92, 0xE8, 0, 0, 0 });              // actual player kingdom
        code.Add(0x52);                                                        // push player
        code.Add(0x56);                                                        // push slot
        int assignmentHostilityCall = AddCall(code);
        AddCounterIncrement(code, counters, CounterProvokedAssignments);
        int assignmentStockJump = AddJump(code);

        int setKingdomFailedOffset = code.Count;
        code.AddRange(new byte[] { 0xC7, 0x04, 0xF5 });                        // clear target state
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.AddRange(new byte[] { 0, 0, 0, 0 });
        code.AddRange(new byte[] { 0xC7, 0x04, 0xF5 });                        // clear family state
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.AddRange(new byte[] { 0, 0, 0, 0 });
        AddCounterIncrement(code, counters, CounterSetKingdomFailures);
        int failureStockJump = AddJump(code);

        int existingOffset = code.Count;
        AddCounterIncrement(code, counters, CounterExistingProvokedOrders);
        code.Add(0x52);                                                        // push actual player kingdom
        code.Add(0x51);                                                        // push existing slot
        int existingHostilityCall = AddCall(code);
        int existingStockJump = AddJump(code);

        int stockOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        code.AddRange(original);                                               // stock mov ecx,context
        int returnJump = AddJump(code);

        IntPtr stockAddress = Add(stub, stockOffset);
        foreach (int jump in stockJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, stockAddress);
            else
                PatchConditionalJump(code, stub, jump, stockAddress);
        }
        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        PatchConditionalJump(code, stub, existingJump, Add(stub, existingOffset));
        PatchConditionalJump(code, stub, existingLoopJump, Add(stub, existingLoopOffset));
        PatchConditionalJump(code, stub, freeFoundJump, Add(stub, freeFoundOffset));
        PatchConditionalJump(code, stub, freeLoopJump, Add(stub, freeLoopOffset));
        PatchJump(code, stub, exhaustedStockJump, stockAddress);
        PatchConditionalJump(code, stub, missingReserveJump, Add(stub, setKingdomFailedOffset));
        PatchCall(code, stub, provokedRelationCall, relationWorker);
        PatchCall(code, stub, setKingdomCall, Add(imageBase, ActorSetKingdomRva));
        PatchConditionalJump(
            code, stub, setKingdomFailedJump, Add(stub, setKingdomFailedOffset));
        PatchCall(code, stub, assignmentHostilityCall, hostilityWorker);
        PatchJump(code, stub, assignmentStockJump, stockAddress);
        PatchJump(code, stub, failureStockJump, stockAddress);
        PatchCall(code, stub, existingHostilityCall, hostilityWorker);
        PatchJump(code, stub, existingStockJump, stockAddress);
        PatchJump(code, stub, returnJump, Add(target, original.Length));
        return code.ToArray();
    }

    private static void InstallActorCommandKillProvocationHook(
        IntPtr process,
        IntPtr imageBase,
        IntPtr counters,
        IntPtr state,
        IntPtr hostilityWorker,
        IntPtr relationWorker,
        string logPath)
    {
        IntPtr target = Add(imageBase, ActorCommandExecuteRva);
        byte[] expected = { 0x56, 0x8B, 0xF1, 0x8B, 0x4E, 0x08 };
        byte[] original = ReadBytes(process, target, expected.Length);
        if (!original.SequenceEqual(expected))
            throw new InvalidOperationException(
                "Не совпал сетевой обработчик ActorCommand kill 1.3.68: " +
                BitConverter.ToString(original));

        IntPtr stub = VirtualAllocEx(
            process, IntPtr.Zero, 8192,
            MemCommit | MemReserve, PageExecuteReadWrite);
        if (stub == IntPtr.Zero)
            ThrowWin32("VirtualAllocEx(ActorCommand kill provocation stub)");
        byte[] code = BuildActorCommandKillProvocationStub(
            imageBase,
            target,
            original,
            counters,
            state,
            hostilityWorker,
            relationWorker,
            stub);
        WriteBytes(process, stub, code);
        if (!FlushInstructionCache(process, stub, code.Length))
            ThrowWin32("FlushInstructionCache(ActorCommand kill provocation stub)");

        byte[] detour = new byte[expected.Length];
        detour[0] = 0xE9;
        Buffer.BlockCopy(
            BitConverter.GetBytes(RelativeBranch(target, stub)),
            0, detour, 1, 4);
        detour[5] = 0x90;
        WriteCodePatch(
            process,
            target,
            detour,
            "ActorCommand kill neutral-site provocation detour");
        AppendLog(
            logPath,
            "ACTORCOMMAND KILL PROVOCATION HOOK PATCHED target=0x" +
            target.ToInt64().ToString("X8") +
            " stub=0x" + stub.ToInt64().ToString("X8") +
            " trigger=TellActorCommandOrder::Execute.");
    }

    private static byte[] BuildActorCommandKillProvocationStub(
        IntPtr imageBase,
        IntPtr target,
        byte[] original,
        IntPtr counters,
        IntPtr state,
        IntPtr hostilityWorker,
        IntPtr relationWorker,
        IntPtr stub)
    {
        List<byte> code = new List<byte>();
        List<int> stockJumps = new List<int>();
        List<int> eligibleJumps = new List<int>();

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterActorCommandOrders);
        code.AddRange(new byte[] { 0x8B, 0x6C, 0x24, 0x18 });                  // mov ebp,saved ECX order
        code.AddRange(new byte[] { 0x85, 0xED });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x75, 0x04 });                        // mov esi,[order+4] command
        code.AddRange(new byte[] { 0x85, 0xF6 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0xF6, 0x46, 0x0C, 0x01 });                  // test target-actor flag
        stockJumps.Add(AddConditionalJump(code, 0x84));
        AddCounterIncrement(code, counters, CounterActorCommandKillOrders);
        code.AddRange(new byte[] { 0x8B, 0x55, 0x08 });                        // mov edx,[order+8] source actor
        code.AddRange(new byte[] { 0x85, 0xD2 });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x8B, 0x46, 0x10 });                        // mov eax,[command+10] target actor id
        code.Add(0x50);                                                        // push eax
        code.AddRange(new byte[] { 0x8B, 0x0D });                              // mov ecx,[actor lookup manager]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, ActorLookupManagerRva).ToInt32()));
        int actorLookupCall = AddCall(code);
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0xF8 });                              // mov edi,eax target actor
        AddCounterIncrement(code, counters, CounterActorCommandTargetsResolved);

        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0xB8, 0x54, 0x01, 0, 0,
            RequiredCompleteKingdomCount });
        stockJumps.Add(AddConditionalJump(code, 0x82));
        code.AddRange(new byte[] { 0x8B, 0x98, 0x50, 0x01, 0, 0 });           // mov ebx,complete kingdoms
        code.AddRange(new byte[] { 0x85, 0xDB });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0xAF, 0xE8, 0, 0, 0 });              // mov ebp,[target+E8] owner
        code.AddRange(new byte[] { 0x85, 0xED });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x44, 0x24, 0x18 });                  // mov eax,saved ECX order
        code.AddRange(new byte[] { 0x8B, 0x50, 0x08 });                        // mov edx,[order+8] source actor
        code.AddRange(new byte[] { 0x8B, 0x92, 0xE8, 0, 0, 0 });              // mov edx,[source+E8]
        code.AddRange(new byte[] { 0x85, 0xD2 });
        stockJumps.Add(AddConditionalJump(code, 0x84));

        code.AddRange(new byte[] { 0x33, 0xC9 });                              // xor ecx,ecx
        int existingLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x3B, 0x6C, 0x8B,
            ReserveFirstKingdomIndex * 4 });                                  // cmp owner,[kingdoms+index*4+reserve]
        int existingJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int existingLoopJump = AddConditionalJump(code, 0x8C);

        foreach (int familyIndex in new int[] { 16, 18, 19, 20, 21, 22 })
        {
            code.AddRange(new byte[] { 0x3B, 0x6B, (byte)(familyIndex * 4) });
            eligibleJumps.Add(AddConditionalJump(code, 0x84));
        }
        stockJumps.Add(AddJump(code));

        int eligibleOffset = code.Count;
        AddCounterIncrement(code, counters, CounterEligibleNeutralTargets);
        code.AddRange(new byte[] { 0x33, 0xC9 });
        int freeLoopOffset = code.Count;
        code.AddRange(new byte[] { 0x83, 0x3C, 0xCD });
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.Add(0x00);
        int freeFoundJump = AddConditionalJump(code, 0x84);
        code.Add(0x41);
        code.AddRange(new byte[] { 0x83, 0xF9, ReserveKingdomCount });
        int freeLoopJump = AddConditionalJump(code, 0x8C);
        AddCounterIncrement(code, counters, CounterProvokedSlotsExhausted);
        int exhaustedStockJump = AddJump(code);

        int freeFoundOffset = code.Count;
        code.AddRange(new byte[] { 0x8B, 0xF1 });                              // mov esi,ecx slot
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });                                  // mov eax,reserve kingdom
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int missingReserveJump = AddConditionalJump(code, 0x84);
        code.AddRange(new byte[] { 0x89, 0x3C, 0xF5 });
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));                // state[slot].target=edi
        code.AddRange(new byte[] { 0x89, 0x2C, 0xF5 });
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));        // state[slot].family=ebp
        code.Add(0x55);
        code.Add(0x50);
        int provokedRelationCall = AddCall(code);

        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });
        code.Add(0x50);
        code.AddRange(new byte[] { 0x8B, 0xCF });
        int setKingdomCall = AddCall(code);
        code.AddRange(new byte[] { 0x8B, 0x44, 0xB3,
            ReserveFirstKingdomIndex * 4 });
        code.AddRange(new byte[] { 0x39, 0x87, 0xE8, 0, 0, 0 });
        int setKingdomFailedJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x8B, 0x44, 0x24, 0x18 });                  // reload order
        code.AddRange(new byte[] { 0x8B, 0x50, 0x08 });                        // source actor
        code.AddRange(new byte[] { 0x8B, 0x92, 0xE8, 0, 0, 0 });              // actual player kingdom
        code.Add(0x52);
        code.Add(0x56);                                                        // slot
        int assignmentHostilityCall = AddCall(code);
        AddCounterIncrement(code, counters, CounterProvokedAssignments);
        int assignmentStockJump = AddJump(code);

        int setKingdomFailedOffset = code.Count;
        code.AddRange(new byte[] { 0xC7, 0x04, 0xF5 });
        code.AddRange(BitConverter.GetBytes(state.ToInt32()));
        code.AddRange(new byte[] { 0, 0, 0, 0 });
        code.AddRange(new byte[] { 0xC7, 0x04, 0xF5 });
        code.AddRange(BitConverter.GetBytes(Add(state, 4).ToInt32()));
        code.AddRange(new byte[] { 0, 0, 0, 0 });
        AddCounterIncrement(code, counters, CounterSetKingdomFailures);
        int failureStockJump = AddJump(code);

        int existingOffset = code.Count;
        AddCounterIncrement(code, counters, CounterExistingProvokedOrders);
        code.Add(0x52);                                                        // actual player kingdom
        code.Add(0x51);                                                        // existing slot
        int existingHostilityCall = AddCall(code);
        int existingStockJump = AddJump(code);

        int stockOffset = code.Count;
        code.Add(0x61);
        code.Add(0x9D);
        code.AddRange(original);
        int returnJump = AddJump(code);

        IntPtr stockAddress = Add(stub, stockOffset);
        foreach (int jump in stockJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, stockAddress);
            else
                PatchConditionalJump(code, stub, jump, stockAddress);
        }
        foreach (int jump in eligibleJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, eligibleOffset));
        PatchCall(code, stub, actorLookupCall, Add(imageBase, ActorLookupByIdRva));
        PatchConditionalJump(code, stub, existingJump, Add(stub, existingOffset));
        PatchConditionalJump(code, stub, existingLoopJump, Add(stub, existingLoopOffset));
        PatchConditionalJump(code, stub, freeFoundJump, Add(stub, freeFoundOffset));
        PatchConditionalJump(code, stub, freeLoopJump, Add(stub, freeLoopOffset));
        PatchJump(code, stub, exhaustedStockJump, stockAddress);
        PatchConditionalJump(code, stub, missingReserveJump, Add(stub, setKingdomFailedOffset));
        PatchCall(code, stub, provokedRelationCall, relationWorker);
        PatchCall(code, stub, setKingdomCall, Add(imageBase, ActorSetKingdomRva));
        PatchConditionalJump(
            code, stub, setKingdomFailedJump, Add(stub, setKingdomFailedOffset));
        PatchCall(code, stub, assignmentHostilityCall, hostilityWorker);
        PatchJump(code, stub, assignmentStockJump, stockAddress);
        PatchJump(code, stub, failureStockJump, stockAddress);
        PatchCall(code, stub, existingHostilityCall, hostilityWorker);
        PatchJump(code, stub, existingStockJump, stockAddress);
        PatchJump(code, stub, returnJump, Add(target, original.Length));
        return code.ToArray();
    }
#endif

    private static int AddRaceCompare(
        List<byte> code,
        string value,
        List<StringReference> strings,
        List<int> compareCalls)
    {
        int stringImmediate = AddMovEdiString(code);
        strings.Add(new StringReference(stringImmediate, value));
        compareCalls.Add(AddCall(code));
        code.AddRange(new byte[] { 0x85, 0xC0 });                              // test eax,eax
        return AddConditionalJump(code, 0x85);                                // jnz match
    }
#endif

    private static byte[] BuildFamilyOwnerStub(
        IntPtr imageBase,
        IntPtr target,
        byte[] original,
        IntPtr counters,
        IntPtr stub,
        int siteCounter,
        IntPtr raceRelationWorker)
    {
        List<byte> code = new List<byte>();
        List<int> stockJumps = new List<int>();
        List<int> actorCompareCalls = new List<int>();
        List<int> kingdomSearchJumps = new List<int>();
#if PRESERVE_NONINDEPENDENT_OWNER
        List<int> allowedOwnerJumps = new List<int>();
#endif
        List<StringReference> strings = new List<StringReference>();
#if RACE_RELATIONS
        int raceRelationCall = -1;
#endif

        code.Add(0x9C);                                                        // pushfd
        code.Add(0x60);                                                        // pushad
        AddCounterIncrement(code, counters, CounterEntries);
        AddCounterIncrement(code, counters, siteCounter);

        // 22C7CC restores a serialized GActor owner, not a fresh-map actor.
        // The exact saved pointer is authoritative for old and current saves,
        // including independent owners and the host's multiplayer snapshot.
        if (target == Add(imageBase, InitialKingdomStoreRva))
            stockJumps.Add(AddJump(code));

        // Source kind 2 includes both saved games and recorded films. Native
        // session state 4 identifies playback before actors materialize: films
        // must repeat fresh-map family assignment or denizens/diplomacy desync.
        // Real loaded games retain their owners, including later replacements.
        // Read both fields on every call; never latch state across sessions.
        code.Add(0xA1);                                                        // mov eax,[GSession]
        code.AddRange(BitConverter.GetBytes(Add(imageBase, 0x5F3FE4).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });                              // no session: preserve
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x83, 0x78, 0x64, 0x02 });                  // loaded source
        int freshSourceJump = AddConditionalJump(code, 0x85);
        code.AddRange(new byte[] { 0x83, 0xB8, 0xF0, 0x00, 0x00, 0x00, 0x04 }); // film playback
        stockJumps.Add(AddConditionalJump(code, 0x85));                         // actual save: preserve
        PatchConditionalJump(code, stub, freshSourceJump, Add(stub, code.Count));

        code.AddRange(new byte[] { 0x8B, 0x0C, 0x24 });                        // mov ecx,[esp] saved EDI actor
        code.AddRange(new byte[] { 0x85, 0xC9 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x49, 0x04 });                        // mov ecx,[actor+4] actor data
        code.AddRange(new byte[] { 0x85, 0xC9 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x8B, 0x51, 0x08 });                        // mov edx,[data+8] actor IDS
        code.AddRange(new byte[] { 0x85, 0xD2 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        AddCounterIncrement(code, counters, CounterCandidates);

        for (int mapping = 0; mapping < ActorKingdomMap.GetLength(0); ++mapping)
        {
            int actorStringImmediate = AddMovEdiString(code);
            strings.Add(new StringReference(
                actorStringImmediate, ActorKingdomMap[mapping, 0]));
            actorCompareCalls.Add(AddCall(code));
            code.AddRange(new byte[] { 0x85, 0xC0 });                          // test eax,eax
            int nextMappingJump = AddConditionalJump(code, 0x84);             // jz next mapping
            AddCounterIncrement(code, counters, CounterMappedActors);
            code.Add(0xBB);                                                    // mov ebx,target kingdom index
            code.AddRange(BitConverter.GetBytes(
                GetTargetKingdomIndex(ActorKingdomMap[mapping, 1])));
            kingdomSearchJumps.Add(AddJump(code));
            int nextMappingOffset = code.Count;
            PatchConditionalJump(code, stub, nextMappingJump, Add(stub, nextMappingOffset));
        }
        stockJumps.Add(AddJump(code));

        int kingdomSearchOffset = code.Count;
        code.Add(0xA1);                                                        // mov eax,[GWorld]
        code.AddRange(BitConverter.GetBytes(
            Add(imageBase, GWorldPointerRva).ToInt32()));
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0x98, 0x54, 0x01, 0x00, 0x00 });      // cmp ebx,[eax+154] complete count
        int missingKingdomJump = AddConditionalJump(code, 0x83);              // jae missing
        code.AddRange(new byte[] { 0x8B, 0x80, 0x50, 0x01, 0x00, 0x00 });      // mov eax,[eax+150] complete kingdoms
        code.AddRange(new byte[] { 0x85, 0xC0 });
        stockJumps.Add(AddConditionalJump(code, 0x84));
#if PRESERVE_NONINDEPENDENT_OWNER
        code.AddRange(new byte[] { 0x8B, 0xD0 });                              // mov edx,eax complete kingdoms
#endif
        code.AddRange(new byte[] { 0x8B, 0x04, 0x98 });                        // mov eax,[eax+ebx*4]
        code.AddRange(new byte[] { 0x85, 0xC0 });
        int missingPointerJump = AddConditionalJump(code, 0x84);
#if PRESERVE_NONINDEPENDENT_OWNER
        // A generated independent actor starts under kingdom_indie,
        // kingdom_enemy, or paws_independent_settlements. An untouched actor
        // already assigned to its family is also safe. Any other owner is a
        // saved/captured owner and must be preserved during load/materialization.
        code.AddRange(new byte[] { 0x8B, 0x4C, 0x24, 0x1C });                  // ecx=original owner
        code.AddRange(new byte[] { 0x3B, 0xC8 });                              // original==target family
        allowedOwnerJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0x4A, 0x0C });                        // original==kingdom_indie [3]
        allowedOwnerJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0x4A, 0x10 });                        // original==kingdom_enemy [4]
        allowedOwnerJumps.Add(AddConditionalJump(code, 0x84));
        code.AddRange(new byte[] { 0x3B, 0x4A, 0x14 });                        // original==independent settlements [5]
        allowedOwnerJumps.Add(AddConditionalJump(code, 0x84));
        stockJumps.Add(AddJump(code));
        int allowedOwnerOffset = code.Count;
#endif
        code.AddRange(new byte[] { 0x89, 0x44, 0x24, 0x1C });                  // replace saved EAX kingdom
        AddCounterIncrement(code, counters, CounterRemappedActors);
#if RACE_RELATIONS
        if (raceRelationWorker != IntPtr.Zero)
            raceRelationCall = AddCall(code);                                 // apply after stock relation reset
#endif
        int foundToStockJump = AddJump(code);

        int missingKingdomOffset = code.Count;
        AddCounterIncrement(code, counters, CounterMissingKingdoms);
        int missingToStockJump = AddJump(code);

        int stockOffset = code.Count;
        code.Add(0x61);                                                        // popad
        code.Add(0x9D);                                                        // popfd
        code.AddRange(original);                                               // mov [edi+E8],eax
        int returnJump = AddJump(code);

        int compareOffset = AppendWideStringCompare(code);

        Dictionary<string, int> stringOffsets = new Dictionary<string, int>(
            StringComparer.Ordinal);
        foreach (StringReference reference in strings)
        {
            int stringOffset;
            if (!stringOffsets.TryGetValue(reference.Value, out stringOffset))
            {
                stringOffset = code.Count;
                stringOffsets.Add(reference.Value, stringOffset);
                code.AddRange(Encoding.Unicode.GetBytes(reference.Value + "\0"));
            }
            PatchInt32(code, reference.ImmediateOffset, Add(stub, stringOffset).ToInt32());
        }

        IntPtr compareAddress = Add(stub, compareOffset);
        foreach (int call in actorCompareCalls)
            PatchCall(code, stub, call, compareAddress);
#if RACE_RELATIONS
        if (raceRelationCall >= 0)
            PatchCall(code, stub, raceRelationCall, raceRelationWorker);
#endif
        foreach (int jump in kingdomSearchJumps)
            PatchJump(code, stub, jump, Add(stub, kingdomSearchOffset));
#if PRESERVE_NONINDEPENDENT_OWNER
        foreach (int jump in allowedOwnerJumps)
            PatchConditionalJump(code, stub, jump, Add(stub, allowedOwnerOffset));
#endif

        IntPtr stockAddress = Add(stub, stockOffset);
        foreach (int jump in stockJumps)
        {
            if (code[jump] == 0xE9)
                PatchJump(code, stub, jump, stockAddress);
            else
                PatchConditionalJump(code, stub, jump, stockAddress);
        }
        PatchConditionalJump(code, stub, missingKingdomJump, Add(stub, missingKingdomOffset));
        PatchConditionalJump(code, stub, missingPointerJump, Add(stub, missingKingdomOffset));
        PatchJump(code, stub, foundToStockJump, stockAddress);
        PatchJump(code, stub, missingToStockJump, stockAddress);
        PatchJump(code, stub, returnJump, Add(target, original.Length));

        return code.ToArray();
    }

    private sealed class StringReference
    {
        public readonly int ImmediateOffset;
        public readonly string Value;

        public StringReference(int immediateOffset, string value)
        {
            ImmediateOffset = immediateOffset;
            Value = value;
        }
    }

    private static int AddMovEdiString(List<byte> code)
    {
        code.Add(0xBF);                                                        // mov edi,imm32
        int immediate = code.Count;
        code.AddRange(new byte[4]);
        return immediate;
    }

    private static void AddCounterIncrement(List<byte> code, IntPtr counters, int index)
    {
        code.AddRange(new byte[] { 0xFF, 0x05 });                              // inc dword ptr [imm32]
        code.AddRange(BitConverter.GetBytes(Add(counters, index * 4).ToInt32()));
    }

    private static int AddCall(List<byte> code)
    {
        int offset = code.Count;
        code.Add(0xE8);
        code.AddRange(new byte[4]);
        return offset;
    }

    private static int AddJump(List<byte> code)
    {
        int offset = code.Count;
        code.Add(0xE9);
        code.AddRange(new byte[4]);
        return offset;
    }

    private static int AddConditionalJump(List<byte> code, byte condition)
    {
        int offset = code.Count;
        code.Add(0x0F);
        code.Add(condition);
        code.AddRange(new byte[4]);
        return offset;
    }

    private static int AppendWideStringCompare(List<byte> code)
    {
        int offset = code.Count;
        code.Add(0x56);                                                        // push esi
        code.AddRange(new byte[] { 0x33, 0xF6 });                              // xor esi,esi
        int loopOffset = code.Count;
        code.AddRange(new byte[] { 0x66, 0x8B, 0x04, 0x32 });                  // mov ax,[edx+esi]
        code.AddRange(new byte[] { 0x66, 0x3B, 0x04, 0x37 });                  // cmp ax,[edi+esi]
        int differentJump = AddConditionalJump(code, 0x85);                   // jne different
        code.AddRange(new byte[] { 0x66, 0x85, 0xC0 });                        // test ax,ax
        int equalJump = AddConditionalJump(code, 0x84);                       // jz equal
        code.AddRange(new byte[] { 0x83, 0xC6, 0x02 });                        // add esi,2
        int repeatJump = AddJump(code);
        int differentOffset = code.Count;
        code.AddRange(new byte[] { 0x33, 0xC0 });                              // xor eax,eax
        code.Add(0x5E);                                                        // pop esi
        code.Add(0xC3);                                                        // ret
        int equalOffset = code.Count;
        code.AddRange(new byte[] { 0xB8, 0x01, 0x00, 0x00, 0x00 });            // mov eax,1
        code.Add(0x5E);
        code.Add(0xC3);
        PatchConditionalJump(code, IntPtr.Zero, differentJump, new IntPtr(differentOffset));
        PatchConditionalJump(code, IntPtr.Zero, equalJump, new IntPtr(equalOffset));
        PatchJump(code, IntPtr.Zero, repeatJump, new IntPtr(loopOffset));
        return offset;
    }

    private static void PatchCall(List<byte> code, IntPtr stub, int offset, IntPtr target)
    {
        if (code[offset] != 0xE8)
            throw new InvalidOperationException("Invalid call placeholder.");
        PatchInt32(code, offset + 1, RelativeBranch(Add(stub, offset), target));
    }

    private static void PatchJump(List<byte> code, IntPtr stub, int offset, IntPtr target)
    {
        if (code[offset] != 0xE9)
            throw new InvalidOperationException("Invalid jump placeholder.");
        PatchInt32(code, offset + 1, RelativeBranch(Add(stub, offset), target));
    }

    private static void PatchConditionalJump(
        List<byte> code, IntPtr stub, int offset, IntPtr target)
    {
        if (code[offset] != 0x0F)
            throw new InvalidOperationException("Invalid conditional jump placeholder.");
        long instruction = stub == IntPtr.Zero ? offset : Add(stub, offset).ToInt64();
        long destination = target.ToInt64();
        long displacement = destination - (instruction + 6);
        if (displacement < Int32.MinValue || displacement > Int32.MaxValue)
            throw new InvalidOperationException("Conditional branch is out of range.");
        PatchInt32(code, offset + 2, (int)displacement);
    }

    private static void PatchInt32(List<byte> code, int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        for (int index = 0; index < 4; ++index)
            code[offset + index] = bytes[index];
    }

    private static void MonitorRuntime(
        Process game,
        IntPtr process,
        IntPtr counters,
        IntPtr syncSignal,
        string logPath)
    {
        int[] previous = ReadCounters(process, counters);
        int observedCaptureAttempt = 0, observedCaptureReturn = 0;
        int observedSync = syncSignal == IntPtr.Zero
            ? 0
            : ReadInt32(process, syncSignal);
        AppendLog(
            logPath,
            "MONITOR STARTED " + FormatCounters(previous) +
            " syncSignals=" + observedSync + ".");
        while (!HasExited(game))
        {
            Thread.Sleep(250);
            int[] current;
            try
            {
                current = ReadCounters(process, counters);
            }
            catch
            {
                if (HasExited(game))
                    break;
                throw;
            }
            if (!current.SequenceEqual(previous))
            {
                previous = current;
                AppendLog(logPath, "COUNTERS " + FormatCounters(current) + ".");
            }
            if (syncSignal != IntPtr.Zero)
            {
                byte[] diagnostic = ReadBytes(process, syncSignal, PawSyncDiagnostics.SignalSize);
                int attempt = BitConverter.ToInt32(diagnostic, 16), returned = BitConverter.ToInt32(diagnostic, 20);
                if (attempt != observedCaptureAttempt || returned != observedCaptureReturn)
                {
                    observedCaptureAttempt = attempt; observedCaptureReturn = returned;
                    AppendLog(logPath, "[Paw Sync] nativeCaptureAttempts=" + attempt + " nativeCaptureReturns=" + returned +
                        " epoch=" + BitConverter.ToUInt32(diagnostic, 8) +
                        " firstIndex=" + BitConverter.ToUInt32(diagnostic, 24) +
                        " localRange=" + BitConverter.ToUInt32(diagnostic, 32) + ".." + BitConverter.ToUInt32(diagnostic, 36) +
                        " gameTime=" + BitConverter.ToSingle(diagnostic, 44).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "; see native log for synclog filename or write error.");
                }
                int currentSync = BitConverter.ToInt32(diagnostic, 0);
                if (currentSync != observedSync)
                {
                    observedSync = currentSync;
                    int source = ReadInt32(process, Add(syncSignal, 4));
                    AppendLog(
                        logPath,
                        "[Paw Sync] Network checksum desync #" + currentSync +
                        " source=0x" + unchecked((uint)source).ToString("X8") +
                        ". Game continues.");
                }
            }
        }
        AppendLog(
            logPath,
            "MONITOR STOPPED: game exited; " + FormatCounters(previous) +
            " syncSignals=" + observedSync + ".");
    }

#if SYNC_CONTINUE
    private static void VerifyOneSyncSignal(
        IntPtr process, IntPtr signal, string logPath)
    {
        int current = ReadInt32(process, signal);
        int source = ReadInt32(process, Add(signal, 4));
        if (current != 1 || source != unchecked((int)0x13572468))
            throw new InvalidOperationException(
                "Самопроверка сигнального блока рассинхрона не прошла.");
        AppendLog(
            logPath,
            "[Paw Sync] SELF-TEST signal=" + current +
            " source=0x" + unchecked((uint)source).ToString("X8") + ".");
    }
#endif

    private static int[] ReadCounters(IntPtr process, IntPtr counters)
    {
        byte[] bytes = ReadBytes(process, counters, CounterCount * 4);
        int[] values = new int[CounterCount];
        for (int index = 0; index < values.Length; ++index)
            values[index] = BitConverter.ToInt32(bytes, index * 4);
        return values;
    }

    private static string FormatCounters(int[] values)
    {
        string result = "entries=" + values[CounterEntries] +
            " candidates=" + values[CounterCandidates] +
            " mapped=" + values[CounterMappedActors] +
            " remapped=" + values[CounterRemappedActors] +
            " missingKingdom=" + values[CounterMissingKingdoms] +
            " constructorA=" + values[CounterInitialConstructor] +
            " constructorB=" + values[CounterMaterializedConstructor];
#if RACE_RELATIONS
        result +=
            " racePasses=" + values[CounterRacePasses] +
            " racePlayers=" + values[CounterRacePlayers] +
            " raceNeutralCalls=" + values[CounterRaceNeutralCalls] +
            " raceMissingFamilies=" + values[CounterRaceMissingFamilies] +
            " finalRelationPasses=" + values[CounterFinalRelationPasses];
#if PROVOKED_NEUTRAL_ATTACK
        result +=
            " attackOrders=" + values[CounterAttackOrders] +
            " eligibleNeutralTargets=" + values[CounterEligibleNeutralTargets] +
            " provokedAssignments=" + values[CounterProvokedAssignments] +
            " existingProvokedOrders=" + values[CounterExistingProvokedOrders] +
            " provokedSlotsExhausted=" + values[CounterProvokedSlotsExhausted] +
            " setKingdomFailures=" + values[CounterSetKingdomFailures] +
            " uiAttackOverrides=" + values[CounterUiAttackOverrides] +
            " minimapNeutralFamilyIcons=" +
                values[CounterMinimapNeutralFamilyIcons] +
            " uiCommandValidationOverrides=" +
                values[CounterUiCommandValidationOverrides] +
            " uiGateEntries=" + values[CounterUiGateEntries] +
            " uiGateStockRejected=" + values[CounterUiGateStockRejected] +
            " uiCommandValidationEntries=" +
                values[CounterUiCommandValidationEntries] +
            " uiCommandValidationErrors=" +
                values[CounterUiCommandValidationErrors] +
            " uiCommandKind4Errors=" + values[CounterUiCommandKind4Errors] +
            " uiCommandResolvedActors=" + values[CounterUiCommandResolvedActors] +
            " uiCommandFamilyTargets=" + values[CounterUiCommandFamilyTargets] +
            " lastUiCommandError=" + values[CounterLastUiCommandError] +
            " lastUiCommandKind=" + values[CounterLastUiCommandKind] +
            " uiActorTargetGateEntries=" +
                values[CounterUiActorTargetGateEntries] +
            " uiActorTargetStockRejected=" +
                values[CounterUiActorTargetStockRejected] +
            " uiActorTargetFamilyOverrides=" +
                values[CounterUiActorTargetFamilyOverrides] +
            " actorCommandOrders=" + values[CounterActorCommandOrders] +
            " actorCommandKillOrders=" + values[CounterActorCommandKillOrders] +
            " actorCommandTargetsResolved=" +
                values[CounterActorCommandTargetsResolved] +
            " personalRelationQueries=" +
                values[CounterPersonalRelationQueries] +
            " raceNeutralOverrides=" +
                values[CounterRaceNeutralOverrides] +
            " herdEnemyOverrides=" +
                values[CounterHerdEnemyOverrides] +
            " provokedEnemyOverrides=" +
                values[CounterProvokedEnemyOverrides] +
            " uiImmediateProvocationAttempts=" +
                values[CounterUiImmediateProvocationAttempts] +
            " uiImmediateProvocationAssignments=" +
                values[CounterUiImmediateProvocationAssignments];
#endif
#endif
        return result;
    }

    private static IntPtr WaitForImageBase(Process game, int timeoutMilliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (HasExited(game))
                throw new InvalidOperationException("Kohan II закрылась во время запуска.");
            try
            {
                game.Refresh();
                if (game.MainModule != null && game.MainModule.BaseAddress != IntPtr.Zero)
                    return game.MainModule.BaseAddress;
            }
            catch (Win32Exception)
            {
            }
            Thread.Sleep(50);
        }
        throw new InvalidOperationException("Не удалось определить адрес k2.exe в памяти.");
    }

    private static Process WaitForLaunchedGame(Process initial, int timeoutMilliseconds)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < timeoutMilliseconds)
        {
            foreach (Process candidate in
                Process.GetProcessesByName("k2").OrderByDescending(GetStartTimeSafe))
            {
                try
                {
                    candidate.Refresh();
                    if (!candidate.HasExited && candidate.MainWindowHandle != IntPtr.Zero)
                        return candidate;
                }
                catch
                {
                }
            }
            Thread.Sleep(100);
        }
        if (initial != null && !HasExited(initial))
            return initial;
        throw new InvalidOperationException(
            "Steam не создал рабочий процесс Kohan II за отведённое время.");
    }

    private static DateTime GetStartTimeSafe(Process process)
    {
        try { return process.StartTime; }
        catch { return DateTime.MinValue; }
    }

    private static string ComputeSha256(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    private static byte[] ReadBytes(IntPtr process, IntPtr address, int size)
    {
        byte[] buffer = new byte[size];
        IntPtr read;
        if (!ReadProcessMemory(process, address, buffer, size, out read) ||
            read.ToInt64() != size)
            ThrowWin32("ReadProcessMemory");
        return buffer;
    }

    private static int ReadInt32(IntPtr process, IntPtr address)
    {
        return BitConverter.ToInt32(ReadBytes(process, address, 4), 0);
    }

    private static void WriteInt32(IntPtr process, IntPtr address, int value)
    {
        WriteBytes(process, address, BitConverter.GetBytes(value));
    }

    private static void WriteBytes(IntPtr process, IntPtr address, byte[] bytes)
    {
        IntPtr written;
        if (!WriteProcessMemory(process, address, bytes, bytes.Length, out written) ||
            written.ToInt64() != bytes.Length)
            ThrowWin32("WriteProcessMemory");
    }

    private static void WriteCodePatch(
        IntPtr process, IntPtr address, byte[] bytes, string name)
    {
        uint oldProtection;
        if (!VirtualProtectEx(
            process, address, bytes.Length, PageExecuteReadWrite, out oldProtection))
            ThrowWin32("VirtualProtectEx(" + name + ")");
        try
        {
            WriteBytes(process, address, bytes);
            if (!FlushInstructionCache(process, address, bytes.Length))
                ThrowWin32("FlushInstructionCache(" + name + ")");
        }
        finally
        {
            uint ignored;
            VirtualProtectEx(process, address, bytes.Length, oldProtection, out ignored);
        }
        if (!ReadBytes(process, address, bytes.Length).SequenceEqual(bytes))
            throw new InvalidOperationException("Проверка патча '" + name + "' не прошла.");
    }

    private static int RelativeBranch(IntPtr from, IntPtr to)
    {
        long displacement = to.ToInt64() - (from.ToInt64() + 5);
        if (displacement < Int32.MinValue || displacement > Int32.MaxValue)
            throw new InvalidOperationException("Ветка машинного кода находится слишком далеко.");
        return (int)displacement;
    }

    private static IntPtr Add(IntPtr value, int offset)
    {
        return new IntPtr(value.ToInt64() + offset);
    }

    private static bool HasExited(Process game)
    {
        try { return game.HasExited; }
        catch { return true; }
    }

    private static void AppendLog(string path, string message)
    {
        File.AppendAllText(
            path,
            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " +
                message + Environment.NewLine,
            new UTF8Encoding(false));
    }

    private static void ThrowWin32(string operation)
    {
        throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
    }
}
