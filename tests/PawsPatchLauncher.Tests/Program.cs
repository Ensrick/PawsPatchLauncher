using PawsPatchLauncher;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

TestProcessErrorMode.Enable();
if(args.Length==1&&args[0]=="--rwd")
{
    var rwdRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PawsRwdTests", Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(rwdRoot);
    try { Console.WriteLine($"RWD TOTAL PASS {RwdRelocatedPathsTests.Run(rwdRoot) + RwdArchiveTests.Run(rwdRoot) + await CustomRwdModTests.RunAsync(rwdRoot)}"); }
    finally
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PawsRwdTests")) + Path.DirectorySeparatorChar;
        if (rwdRoot.StartsWith(parent, StringComparison.OrdinalIgnoreCase)) Directory.Delete(rwdRoot, true);
    }
    return;
}
if(args.Length==3&&args[0]=="--verify-installed-rwd") {await CustomRwdModTests.VerifyInstalledAsync(args[1],args[2]);return;}
if(args.Length==5&&args[0]=="--stage-custom-rwd") {await CustomRwdModTests.StageAsync(args[1],args[2],args[3],args[4]=="on");return;}
if(args.Length==4&&args[0]=="--verify-pure-030") {await Pure030Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==1&&args[0]=="--game-user-settings") {GameUserSettingsTests.Run();return;}
// Brings a sandbox copy of a real 1.3.68 game folder up to date (reads the game folder only), for test runs of the copy.
if(args.Length==3&&args[0]=="--sandbox-sync") {var copied=await new GameSandbox(args[1],args[2]).SyncAsync();Console.WriteLine($"SANDBOX COPY {copied} files copied to {args[2]}");return;}
if(args.Length==1&&args[0]=="--sandbox")
{
    var sandboxRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PawsSandboxTests", Guid.NewGuid().ToString("N")));
    Directory.CreateDirectory(sandboxRoot);
    try { await GameSandboxTests.RunAsync(sandboxRoot); }
    finally { Directory.Delete(sandboxRoot, true); }
    return;
}
if(args.Length==4&&args[0]=="--verify-september20") {await September20Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--verify-september-betas") {await SeptemberBetaTests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--verify-palette39") {await Palette39Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--verify-arcane-031") {await Arcane031Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==1&&args[0]=="--chat-order") {ChatActivityTests.Run();ChatFriendActivityTests.Run();return;}
if(args.Length==4&&args[0]=="--verify-arcane-031-beta1") {await Arcane031Beta1Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--verify-apparition-hotfix") {await ApparitionTextTests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--verify-arcane-030") {await Arcane030Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--verify-arcane-beta9") {await ArcaneBeta9Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==4&&args[0]=="--stage-pure-options") {await PureOptionsTests.StageAsync(args[1],args[2],args[3]);return;}
if(args.Length==1&&args[0]=="--pure-options") {PureOptionsTests.Run();return;}
if(args.Length==1&&args[0]=="--color-label-checks") {ColorPaletteReview.Labels();return;}
if(args.Length==3&&args[0]=="--stage-colors48-review") {await ColorPaletteReview.StageAsync(args[1],args[2]);return;}
if(args.Length==4&&args[0]=="--restore-pretest-game") {await ColorPaletteReview.RestoreAsync(args[1],args[2],args[3]);return;}
if(args.Length==1 && args[0]=="--verify-lobby-preferences") {await GameLobbyPreferencesTests.RunAsync();return;}
if(args.Length==3 && args[0]=="--repair-lobby-fixture") {await GameLobbyPreferencesTests.RepairFixtureAsync(args[1],args[2]);return;}
if(args.Length==4 && args[0]=="--stage-language-game") {await EuropeanLanguageTests.StageGameAsync(args[1],args[2],args[3]);return;}
if(args.Length==5 && args[0]=="--stage-language-game") {await EuropeanLanguageTests.StageGameAsync(args[1],args[2],args[3],args[4]);return;}
if(args.Length==3 && args[0]=="--verify-european-languages") {await EuropeanLanguageTests.RunAsync(args[1],args[2]);return;}
if(args.Length==2 && args[0]=="--sound-import-checks") {NotificationImportTests.Run(args[1]);return;}
if(args.Length==2 && args[0]=="--import-sound-files") {NotificationImportTests.Files(args[1]);return;}
if(args.Length==4&&args[0]=="--verify-lobby-beta7") {await LobbyBeta7Tests.RunAsync(args[1],args[2],args[3]);return;}
if(args.Length==3&&args[0]=="--stage-lobby-beta7-review") {await LobbyBeta7Tests.StageReviewAsync(args[1],args[2]);return;}
AiOptionsTests.Run();
if (args.Length == 5 && args[0] == "--verify-prophet-beta6")
{
    await ProphetBeta6Tests.RunAsync(args[1], args[2], args[3], args[4]);
    return;
}
if (args.Length == 4 && args[0] == "--verify-graphics-beta5")
{
    await GraphicsBeta5Tests.RunAsync(args[1], args[2], args[3]);
    return;
}
if (args.Length >= 2 && args[0] == "--verify-game-diagnostics")
{
    await GameDiagnosticsTests.RunAsync(args[1], completeArchive: true);
    return;
}
if (args.Length == 3 && args[0] == "--verify-split-languages")
{
    await SplitLanguageTests.RunAsync(args[1], args[2]);
    return;
}
if (args.Length == 5 && args[0] == "--verify-data-only")
{
    await StartupCompatibilityTests.PackagesAsync(args[1], args[2], args[3], args[4]);
    return;
}
if (args.Length == 2 && args[0] == "--verify-steam-baseline")
{
    await VanillaBootstrapTests.RunSteamAsync(args[1]);
    return;
}
if (args.Length == 1 && args[0] == "--verify-vanilla-bootstrap")
{
    await VanillaBootstrapTests.RunAsync();
    return;
}
if (args.Length == 4 && args[0] == "--verify-mod-modes")
{
    await ModModeTests.RunAsync(args[1], args[2], args[3]);
    return;
}
if (args.Length == 1 && args[0] == "--exit-code-probe")
{
    Environment.ExitCode = int.Parse(Console.ReadLine()!);
    return;
}
if (args.Length == 3 && args[0] == "--verify-shared-guide")
{
    await SharedPatchGuideTests.RunAsync(args[1], args[2]);
    return;
}
if (args.Length == 4 && args[0] == "--verify-roaming-x2")
{
    await RoamingX2PackageTests.RunAsync(args[1], args[2], args[3]);
    return;
}
if (args.Length == 5 && args[0] == "--verify-promotion")
{
    await PromotionTransitionTests.RunAsync(args[1], args[2], args[3], args[4]);
    return;
}
if (args.Length == 2 && args[0] == "--export-guide")
{
    await File.WriteAllTextAsync(args[1], JsonSerializer.Serialize(PatchGuide.Current(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true }));
    return;
}
if (args.Length == 1 && args[0] == "--quiet-mode-check")
{
    Console.WriteLine("QUIET MODE PASS: current-process critical/fault error dialogs disabled; no UI or crash generated.");
    return;
}

if (args.Length == 3 && args[0] == "--audit-combinations")
{
    try { await CombinationAudit.RunAsync(args[1], args[2]); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    return;
}

if (args.Length is 3 or 4 && args[0] == "--audit-city-assistant")
{
    await CombinationAudit.RunAsync(args[1], args[2], cityAssistantOnly: true, cityCandidate: args.Length == 4 ? args[3] : null);
    return;
}

if (args.Length == 5 && args[0] == "--verify-city-transitions")
{
    await CityAssistantTransitionTests.RunAsync(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length is 5 or 6 && args[0] == "--verify-powers-shards")
{
    try { await PowersShardsTests.VerifyPackagesAsync(args[1], args[2], args[3], args[4], args.Length == 6 ? args[5] : null); }
    catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    return;
}

if (args.Length == 5 && args[0] == "--verify-common-ui-profiles")
{
    await CommonUiPackageTests.RunAsync(args[1], args[2], args[3], args[4]);
    return;
}

if (args.Length == 4 && args[0] == "--self-update-fixtures")
{
    await SelfUpdateTests.RunAsync(args[1],args[2],args[3]);
    return;
}

if (args.Length == 4 && args[0] == "--real-launcher-update")
{
    await SelfUpdateTests.RunRealAsync(args[1], args[2], args[3]);
    return;
}

if (args.Length == 2 && args[0] == "--uninstall-helper-fixtures")
{
    await RemovalTests.RunHelperAsync(args[1]);
    return;
}

if (args.Length == 6 && args[0] == "--verify-clean-install")
{
    await CleanInstallTests.RunAsync(args[1], args[2], args[3], args[4], args[5]);
    return;
}

if ((args.Length == 3 || args.Length == 4) && args[0] == "--verify-release")
{
    await VerifyReleaseAsync(args[1], args[2], args.Length == 4 ? args[3] : "stable");
    return;
}

if (args.Length == 4 && args[0] == "--verify-transition")
{
    await VerifyTransitionAsync(args[1], args[2], args[3]);
    return;
}

if (args.Length == 3 && args[0] == "--verify-gameplay-profiles")
{
    await VerifyGameplayProfilesAsync(args[1], args[2]);
    return;
}

var root = Path.Combine(Path.GetTempPath(), "PawsPatchLauncherTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var passed = 0;
passed += ChatMemoryCacheTests.Run();
passed += await ConnectionTests.RunAsync(root);

try
{
        passed += await AtomicStateWriteTests.RunAsync(root);
        passed += await LauncherDownloadRecoveryTests.RunAsync(root);
        passed += await StartupCompatibilityTests.RunAsync(root);
        passed += await AccountTests.RunAsync(root);
        passed += await AccountModerationTests.RunAsync(root);
        passed += await AccountTeamTests.RunAsync(root);
        passed += await HistoryNetworkTests.RunAsync(root);
        passed += ChatActivityTests.Run();
        passed += ChatDividerTests.Run();
        passed += LanguageCatalogTests.Run();
        passed += await CompatibilityModerationTests.RunAsync(root);
        passed += await LauncherSessionTests.RunAsync(root);
        passed += await AccountProfileTests.RunAsync(root);
        passed += await AccountActionsTests.RunAsync(root);
        passed += await SocialTests.RunAsync(root);
        passed += await CommunityTests.RunAsync(root);
        passed += await OfferTests.RunAsync(root);
        passed += await SocialIdentityMediaTests.RunAsync(root);
    passed += await EnhancementTests.RunAsync(root);
    passed += await SocialHubTests.RunAsync(root);
    passed += PromotedReleaseTests.Run();
    passed += OfficialFeedConfigurationTests.Run();
    passed += await ClipboardRetryTests.RunAsync();
    passed += await GameLobbyPreferencesTests.RunAsync();
    passed += await WindowsClipboardTests.RunAsync();
    passed += DiagnosticArchiveHistoryTests.Run(root);
    passed += RwdArchiveTests.Run(root);
    passed += RwdRelocatedPathsTests.Run(root);
    passed += await CustomRwdModTests.RunAsync(root);
    passed += await GameSandboxTests.RunAsync(root);
    passed += await GameDiagnosticsTests.RunAsync(root);
    passed += await LauncherEvolutionTests.RunAsync(root);
    passed += ChannelPresentationTests.Run();
    passed += WindowPlacementTests.Run(root);
    passed += PowersShardsTests.Run();
    passed += EffectiveSettingsTests.Run();
    passed += PawDependencyTests.Run();
    passed += GameActivityTests.Run();
    passed += GameActivityTimingTests.Run();
    passed += GameActivityNativeRaceTests.Run();
    passed += await GameActivityTransportTests.RunAsync(root);
    passed += FriendConfigurationTests.Run();
    passed += FriendCopyPlanTests.Run();
    passed += Launcher080Tests.Run();
    passed += await SocialVersionTests.RunAsync(root);
    passed += await PatchGuideTests.RunAsync(root);
    passed += await LauncherUpdateTests.RunAsync(root);
    passed += ComponentSettingsTests.Run();
    passed += GameUserSettingsTests.Run();
    passed += await ModChannelTests.RunAsync(root);
    passed += PawPatchVersionTests.Run();
    passed += InstalledModVersionTests.Run();
    passed += PawsPatchLauncher.Tests.ChangelogTimelineTests.Run();
    passed += ChatFriendActivityTests.Run();
    ExpectThrows<InvalidDataException>(() => CryptoAndIO.SafeChildPath(root, "..\\escape.txt"));
    passed++;

    var stableFeedPath = Path.Combine(root, "stable.json");
    var betaFeedPath = Path.Combine(root, "beta.json");
    await File.WriteAllTextAsync(stableFeedPath, JsonSerializer.Serialize(new ChannelManifest { Channel = "stable" }, LauncherJsonContext.Default.ChannelManifest));
    await File.WriteAllTextAsync(betaFeedPath, JsonSerializer.Serialize(new ChannelManifest { Channel = "beta" }, LauncherJsonContext.Default.ChannelManifest));
    var channelClient = new FeedClient(new LauncherConfiguration
    {
        FeedUrls = [stableFeedPath],
        BetaFeedUrls = [betaFeedPath],
        CacheRoot = Path.Combine(root, "channel-cache")
    });
    AssertEqual("stable", (await channelClient.GetChannelAsync("stable"))?.Channel);
    AssertEqual("beta", (await channelClient.GetChannelAsync("beta"))?.Channel);
    passed += 2;

    var currentRelease = new PackageRelease { Id = "core", Version = "1.0", Priority = 100, Sha256 = "ABC" };
    var currentState = new InstallState
    {
        Modules = new Dictionary<string, InstalledModule>(StringComparer.OrdinalIgnoreCase)
        {
            ["core"] = new InstalledModule { Version = "1.0", Priority = 100, ArchiveSha256 = "ABC", Enabled = true }
        }
    };
    AssertTrue(!UpdateDetector.HasModuleChanges(currentState, [currentRelease]), "Current packages were reported as outdated.");
    AssertTrue(UpdateDetector.HasModuleChanges(currentState, [new PackageRelease { Id = "core", Version = "1.0", Priority = 100, Sha256 = "DEF" }]), "A text-only archive revision with the same patch version was not detected.");
    AssertTrue(UpdateDetector.HasModuleChanges(currentState, [new PackageRelease { Id = "core", Version = "1.1", Priority = 100, Sha256 = "DEF" }]), "A newer package was not detected.");
    AssertTrue(UpdateDetector.HasModuleChanges(currentState, []), "A module removed from the selected channel was not detected.");
    passed += 4;

    var configurationCode = ConfigurationCode.Create(new UserSettings
    {
        Channel = "beta",
        IndependentHostility = true,
        RoamingSpawnMode = "standard",
        AdditionalRoamingCompanies = false,
        SiegeBalance = true,
        LargeMapSizes = false,
        RussianLocalization = true,
        CustomPlayerColors = false,
        DesyncMode = "continue"
    });
    AssertEqual("PAW-BETA-IW1-SP1-RM0-SG1-LM1-RU1-CL0-OOS1-AI1", configurationCode);
    passed++;

    var executableConfiguration = new LauncherConfiguration
    {
        PreferredGameExecutable = "k2_paws_family_herd_relations_1372.exe"
    };
    AssertEqual("k2.exe", GameExecutableSelector.Select(executableConfiguration, false, false, false));
    AssertEqual("k2_paws_family_herd_relations_1372.exe", GameExecutableSelector.Select(executableConfiguration, false, false, true));
    AssertEqual("k2_paws_sync_continue_1372.exe", GameExecutableSelector.Select(executableConfiguration, false, true, false));
    AssertEqual("k2_paws_sync_family_herd_relations_1372.exe", GameExecutableSelector.Select(executableConfiguration, false, true, true));
    AssertEqual("k2_paws_lobby_colors_mp_1372_experimental.exe", GameExecutableSelector.Select(executableConfiguration, true, false, true));
    AssertEqual("k2_paws_lobby_colors_mp_sync_1372.exe", GameExecutableSelector.Select(executableConfiguration, true, true, true));
    passed += 6;

    var commonUiChannel = new ChannelManifest
    {
        Packages = [new PackageRelease { Id = "common-ui", Required = true }, new PackageRelease { Id = "player-colors" }]
    };
    AssertTrue(GameExecutableSelector.HasCommonUi(commonUiChannel), "Required common UI module not recognized.");
    AssertTrue(!GameExecutableSelector.HasCommonUi(new ChannelManifest()), "Legacy channel must keep its stock fallback.");
    AssertEqual("k2_paws_ui_1372.exe", GameExecutableSelector.Select(executableConfiguration, false, false, false, true));
    passed += 3;
    foreach (var colors in new[] { false, true })
    foreach (var bypass in new[] { false, true })
    foreach (var hostility in new[] { false, true })
    {
        var settings = new UserSettings { DesyncMode = "official", RoamingSpawnMode = "x4", AdditionalRoamingCompanies = true, SiegeBalance = true };
        var selected = GamePackageSelector.Select(commonUiChannel, settings, false, colors);
        AssertTrue(selected.Any(p => p.Id == "common-ui"), "Common UI was excluded by a profile.");
        AssertTrue(GameExecutableSelector.Select(executableConfiguration, colors, bypass, hostility, true) != "k2.exe", "Profile bypassed common UI loader.");
        passed += 2;
    }

    var fingerprintFeed = new ChannelManifest
    {
        Channel = "stable",
        Game = new GameRequirement { Version = "1.3.72", SteamBuild = "25068126", K2ExeSha256 = ["GAME"] },
        Packages = [new PackageRelease { Id = "core", Version = "1.0", Priority = 100, Size = 10, Sha256 = "HASH" }]
    };
    var stableFingerprint = ChannelFingerprint.Create(fingerprintFeed);
    AssertEqual(stableFingerprint, ChannelFingerprint.Create(fingerprintFeed));
    fingerprintFeed.Channel = "beta";
    AssertTrue(stableFingerprint != ChannelFingerprint.Create(fingerprintFeed), "Changing the channel did not change its fingerprint.");
    fingerprintFeed.Channel = "stable";
    fingerprintFeed.Packages[0].Sha256 = "NEW-HASH";
    AssertTrue(stableFingerprint != ChannelFingerprint.Create(fingerprintFeed), "Changing a package did not change the channel fingerprint.");
    passed += 3;

    var changelogFeed = new ChannelManifest
    {
        Channel = "beta",
        Changelog =
        [
            new ChangelogEntry
            {
                Category = "launcher",
                Version = "0.4.4",
                PublishedAt = "2026-09-05",
                Title = new LocalizedText { Ru = "История изменений", En = "Changelog" },
                Body = new LocalizedText { Ru = "Все записи", En = "Every entry" }
            }
        ]
    };
    var changelogJson = JsonSerializer.Serialize(changelogFeed, LauncherJsonContext.Default.ChannelManifest);
    var changelogRoundTrip = JsonSerializer.Deserialize(changelogJson, LauncherJsonContext.Default.ChannelManifest);
    AssertEqual(1, changelogRoundTrip?.Changelog.Count);
    AssertEqual("launcher", changelogRoundTrip?.Changelog[0].Category);
    AssertEqual("Every entry", changelogRoundTrip?.Changelog[0].Body.Get("en"));
    passed += 3;

    var game = Path.Combine(root, "game");
    Directory.CreateDirectory(game);
    await File.WriteAllTextAsync(Path.Combine(game, "shared.txt"), "original");

    var basePackage = await CreatePackageAsync(root, "arcane-wars", "1.0.0", 0,
        new Dictionary<string, string> { ["shared.txt"] = "arcane", ["base-only.txt"] = "base" });
    var patchPackage = await CreatePackageAsync(root, "pawpatch-core", "2.0.0", 100,
        new Dictionary<string, string> { ["shared.txt"] = "patched", ["patch-only.txt"] = "patch" });

    var cacheClient = new FeedClient(new LauncherConfiguration { CacheRoot = Path.Combine(root, "cache-check") });
    AssertTrue(!cacheClient.IsPackageCached(basePackage.Release), "A package was reported cached before download.");
    await cacheClient.DownloadVerifiedAsync(basePackage.Release, null);
    AssertTrue(cacheClient.IsPackageCached(basePackage.Release), "A downloaded package was not reported cached.");
    passed += 2;

    var installer = new ModuleInstaller(game);
    var baseInstalled = await installer.PrepareAsync(basePackage.Release, basePackage.Archive);
    var patchInstalled = await installer.PrepareAsync(patchPackage.Release, patchPackage.Archive);
    await installer.ReconcileAsync(new Dictionary<string, InstalledModule>
    {
        [basePackage.Release.Id] = baseInstalled,
        [patchPackage.Release.Id] = patchInstalled
    });
    AssertEqual("patched", await File.ReadAllTextAsync(Path.Combine(game, "shared.txt")));
    AssertEqual("patch", await File.ReadAllTextAsync(Path.Combine(game, "patch-only.txt")));
    passed += 2;

    await installer.ReconcileAsync(new Dictionary<string, InstalledModule>
    {
        [basePackage.Release.Id] = baseInstalled
    });
    AssertEqual("arcane", await File.ReadAllTextAsync(Path.Combine(game, "shared.txt")));
    AssertTrue(!File.Exists(Path.Combine(game, "patch-only.txt")), "Disabled overlay file was not removed.");
    passed += 2;

    var startupGame = Path.Combine(root, "startup-game");
    Directory.CreateDirectory(startupGame);
    var startupPackage = await CreatePackageAsync(root, "startup-base", "1.0.0", 50,
        new Dictionary<string, string> { ["startup\\autoexec.txt"] = "adddepot %USERDATA%/data/ 1" });
    var localizationPackage = await CreatePackageAsync(root, "localization-ru", "1.0.0", 200,
        new Dictionary<string, string>
        {
            ["startup\\autoexec.txt"] = "adddepot %USERDATA%/data/ 1\naddlocaledepot localized/RU/Local_ru.rwd",
            ["startup\\autoexec_ru.txt"] = "addlocaledepot Local_ru.rwd"
        });
    var startupInstaller = new ModuleInstaller(startupGame);
    var startupInstalled = await startupInstaller.PrepareAsync(startupPackage.Release, startupPackage.Archive);
    var localizationInstalled = await startupInstaller.PrepareAsync(localizationPackage.Release, localizationPackage.Archive);
    await startupInstaller.ReconcileAsync(new Dictionary<string, InstalledModule>
    {
        [startupPackage.Release.Id] = startupInstalled,
        [localizationPackage.Release.Id] = localizationInstalled
    });
    AssertTrue((await File.ReadAllTextAsync(Path.Combine(startupGame, "startup", "autoexec.txt"))).Contains("addlocaledepot"),
        "Russian localization did not override the base startup configuration.");
    await startupInstaller.ReconcileAsync(new Dictionary<string, InstalledModule>
    {
        [startupPackage.Release.Id] = startupInstalled
    });
    var restoredStartup = await File.ReadAllTextAsync(Path.Combine(startupGame, "startup", "autoexec.txt"));
    AssertTrue(restoredStartup.Contains("adddepot %USERDATA%/data/ 1"), "Disabling localization removed the required work depot.");
    AssertTrue(!restoredStartup.Contains("addlocaledepot"), "Disabling localization kept the Russian localized depot.");
    AssertTrue(!File.Exists(Path.Combine(startupGame, "startup", "autoexec_ru.txt")), "Disabling localization kept autoexec_ru.txt.");
    passed += 4;

    var verifyErrors = await installer.VerifyAsync();
    AssertEqual(0, verifyErrors.Count);
    passed++;

    await File.WriteAllTextAsync(Path.Combine(game, "shared.txt"), "tampered");
    verifyErrors = await installer.VerifyAsync();
    AssertTrue(verifyErrors.Any(x => x.Contains("shared.txt", StringComparison.OrdinalIgnoreCase)), "Tampering was not detected.");
    passed++;

    var adoptedGame = Path.Combine(root, "adopted-game");
    Directory.CreateDirectory(adoptedGame);
    await File.WriteAllTextAsync(Path.Combine(adoptedGame, "shared.txt"), "patched");
    var adoptedInstaller = new ModuleInstaller(adoptedGame);
    var adoptedBase = await adoptedInstaller.PrepareAsync(basePackage.Release, basePackage.Archive);
    var adoptedPatch = await adoptedInstaller.PrepareAsync(patchPackage.Release, patchPackage.Archive);
    await adoptedInstaller.ReconcileAsync(new Dictionary<string, InstalledModule>
    {
        [basePackage.Release.Id] = adoptedBase,
        [patchPackage.Release.Id] = adoptedPatch
    });
    await adoptedInstaller.ReconcileAsync(new Dictionary<string, InstalledModule> { [basePackage.Release.Id] = adoptedBase });
    AssertEqual("arcane", await File.ReadAllTextAsync(Path.Combine(adoptedGame, "shared.txt")));
    passed++;

    var cleanupGame = Path.Combine(root, "cleanup-game");
    Directory.CreateDirectory(cleanupGame);
    await File.WriteAllTextAsync(Path.Combine(cleanupGame, "obsolete.txt"), "manual legacy file");
    var cleanupPackage = await CreatePackageAsync(root, "cleanup", "1.0.0", 500,
        new Dictionary<string, string>(), ["obsolete.txt"]);
    var cleanupInstaller = new ModuleInstaller(cleanupGame);
    var cleanupInstalled = await cleanupInstaller.PrepareAsync(cleanupPackage.Release, cleanupPackage.Archive);
    await cleanupInstaller.ReconcileAsync(new Dictionary<string, InstalledModule> { [cleanupPackage.Release.Id] = cleanupInstalled });
    AssertTrue(!File.Exists(Path.Combine(cleanupGame, "obsolete.txt")), "A declared obsolete file was not removed.");
    AssertEqual(0, (await cleanupInstaller.VerifyAsync()).Count);
    await cleanupInstaller.ReconcileAsync(new Dictionary<string, InstalledModule>());
    AssertEqual("manual legacy file", await File.ReadAllTextAsync(Path.Combine(cleanupGame, "obsolete.txt")));
    passed += 3;

    passed += await ReliabilityTests.RunAsync(root);
    passed += await RemovalTests.RunAsync(root);
    passed += await ModLibraryTests.RunAsync(root);
    passed += PureFixesModeTests.Run();
    passed += PureOptionsTests.Run();
    passed += PureBetaTests.Run();
    Console.WriteLine($"PASS {passed}");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}

static async Task<(PackageRelease Release, string Archive)> CreatePackageAsync(string root, string id, string version, int priority,
    Dictionary<string, string> files, IEnumerable<string>? remove = null)
{
    var source = Path.Combine(root, "source-" + id);
    var payload = Path.Combine(source, "payload");
    Directory.CreateDirectory(payload);
    var manifest = new ModuleArchiveManifest { Id = id, Version = version, Remove = remove?.ToList() ?? [] };
    foreach (var pair in files)
    {
        var path = Path.Combine(payload, pair.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, pair.Value);
        var bytes = await File.ReadAllBytesAsync(path);
        manifest.Files.Add(new ModuleFile { Path = pair.Key, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
    }
    await File.WriteAllTextAsync(Path.Combine(source, "module.json"), JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.ModuleArchiveManifest));
    var archive = Path.Combine(root, id + ".zip");
    ZipFile.CreateFromDirectory(source, archive, CompressionLevel.SmallestSize, false);
    var hash = await CryptoAndIO.Sha256Async(archive);
    return (new PackageRelease { Id = id, Version = version, Priority = priority, Sha256 = hash, Size = new FileInfo(archive).Length, Urls = [archive] }, archive);
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}.");
}

static void AssertTrue(bool value, string message)
{
    if (!value) throw new Exception(message);
}

static void ExpectThrows<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}

static async Task VerifyReleaseAsync(string feedPath, string publicKeyPath, string channelName)
{
    var root = Path.Combine(Path.GetTempPath(), "PawsPatchLauncherReleaseTests", Guid.NewGuid().ToString("N"));
    var game = Path.Combine(root, "Kohan II");
    Directory.CreateDirectory(game);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(game, "k2.exe"), "test sentinel; never distributed");
        var configuration = new LauncherConfiguration
        {
            FeedUrls = [Path.GetFullPath(feedPath)],
            PublicKeyPem = await File.ReadAllTextAsync(Path.GetFullPath(publicKeyPath)),
            RequireSignedRemoteFeed = true,
            CacheRoot = Path.Combine(root, "cache")
        };
        var client = new FeedClient(configuration);
        if (channelName.Equals("beta", StringComparison.OrdinalIgnoreCase)) configuration.BetaFeedUrls = [Path.GetFullPath(feedPath)];
        var channel = await client.GetChannelAsync(channelName) ?? throw new Exception("Release feed was not loaded.");
        var installer = new ModuleInstaller(game);
        var modules = new Dictionary<string, InstalledModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in channel.Packages.OrderBy(x => x.Priority))
        {
            var archive = await client.DownloadVerifiedAsync(package, null);
            modules[package.Id] = await installer.PrepareAsync(package, archive);
        }
        await installer.ReconcileAsync(modules);
        var errors = await installer.VerifyAsync();
        if (errors.Count != 0) throw new Exception("Release verification failed: " + string.Join("; ", errors.Take(10)));
        var state = installer.LoadState();
        var declared = state.Modules.Sum(x => x.Value.Files.Count);
        var installed = Directory.EnumerateFiles(game, "*", SearchOption.AllDirectories)
            .Count(x => !x.Contains(Path.DirectorySeparatorChar + ".pawpatch" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        Console.WriteLine($"RELEASE PASS modules={state.Modules.Count} declaredFiles={declared} gameFiles={installed}");
    }
    finally
    {
        var full = Path.GetFullPath(root);
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PawsPatchLauncherReleaseTests")) + Path.DirectorySeparatorChar;
        if (full.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
    }
}

static async Task VerifyTransitionAsync(string betaFeedPath, string stableFeedPath, string publicKeyPath)
{
    var root = Path.Combine(Path.GetTempPath(), "PawsPatchLauncherTransitionTests", Guid.NewGuid().ToString("N"));
    var game = Path.Combine(root, "Kohan II");
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(game);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(game, "k2.exe"), "test sentinel; never distributed");
        var configuration = new LauncherConfiguration
        {
            FeedUrls = [Path.GetFullPath(stableFeedPath)],
            BetaFeedUrls = [Path.GetFullPath(betaFeedPath)],
            PublicKeyPem = await File.ReadAllTextAsync(Path.GetFullPath(publicKeyPath)),
            RequireSignedRemoteFeed = true,
            CacheRoot = cache
        };
        var client = new FeedClient(configuration);
        var installer = new ModuleInstaller(game);

        async Task ApplyAsync(string channelName)
        {
            var channel = await client.GetChannelAsync(channelName) ?? throw new Exception($"{channelName} feed was not loaded.");
            var modules = new Dictionary<string, InstalledModule>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in channel.Packages.OrderBy(x => x.Priority))
            {
                var archive = await client.DownloadVerifiedAsync(package, null);
                modules[package.Id] = await installer.PrepareAsync(package, archive);
            }
            await installer.ReconcileAsync(modules);
            var errors = await installer.VerifyAsync();
            if (errors.Count != 0) throw new Exception($"{channelName} verification failed: " + string.Join("; ", errors.Take(10)));
        }

        var colorFiles = new[]
        {
            "k2_paws_lobby_colors_mp_1372_experimental.exe",
            "paws_player_colors.ini",
            Path.Combine("Data", "UI", "Menus", "pcolors.tgi")
        };
        await ApplyAsync("beta");
        foreach (var relative in colorFiles) AssertTrue(File.Exists(Path.Combine(game, relative)), $"Beta color file is missing: {relative}");
        await ApplyAsync("stable");
        foreach (var relative in colorFiles) AssertTrue(!File.Exists(Path.Combine(game, relative)), $"Stable rollback left a beta color file: {relative}");
        Console.WriteLine("TRANSITION PASS beta->stable removed 3 beta color files");
        await new PatchRecovery(game).RollbackAsync(installer.LoadState());
        foreach (var relative in colorFiles) AssertTrue(File.Exists(Path.Combine(game, relative)), $"Patch rollback did not restore {relative}");
        AssertEqual(0, (await installer.VerifyAsync()).Count);
        Console.WriteLine("ROLLBACK PASS stable->previous beta restored all beta files and hashes");
    }
    finally
    {
        var full = Path.GetFullPath(root);
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PawsPatchLauncherTransitionTests")) + Path.DirectorySeparatorChar;
        if (full.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
    }
}

static async Task VerifyGameplayProfilesAsync(string feedPath, string publicKeyPath)
{
    var root = Path.Combine(Path.GetTempPath(), "PawsPatchLauncherProfileTests", Guid.NewGuid().ToString("N"));
    var game = Path.Combine(root, "Kohan II");
    Directory.CreateDirectory(game);
    try
    {
        await File.WriteAllTextAsync(Path.Combine(game, "k2.exe"), "test sentinel; never distributed");
        var configuration = new LauncherConfiguration
        {
            FeedUrls = [Path.GetFullPath(feedPath)],
            PublicKeyPem = await File.ReadAllTextAsync(Path.GetFullPath(publicKeyPath)),
            RequireSignedRemoteFeed = true,
            CacheRoot = Path.Combine(root, "cache")
        };
        var client = new FeedClient(configuration);
        var channel = await client.GetChannelAsync("stable") ?? throw new Exception("Profile feed was not loaded.");
        var installer = new ModuleInstaller(game);
        var prepared = new Dictionary<string, InstalledModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in channel.Packages.Where(package =>
                     package.Required
                     || package.Id.StartsWith("roaming-profile-", StringComparison.OrdinalIgnoreCase)
                     || package.Id is "siege-balance-standard" or "large-map-sizes-standard"))
        {
            var archive = await client.DownloadVerifiedAsync(package, null);
            prepared[package.Id] = await installer.PrepareAsync(package, archive);
        }

        var required = channel.Packages.Where(package => package.Required).Select(package => package.Id).ToArray();
        var profiles = new[]
        {
            new[] { "default-x4-with-new" },
            new[] { "standard-with-new", "roaming-profile-standard-with-new", "siege-balance-standard", "large-map-sizes-standard" },
            new[] { "x4-no-new", "roaming-profile-x4-no-new" },
            new[] { "standard-no-new", "roaming-profile-standard-no-new", "siege-balance-standard", "large-map-sizes-standard" }
        };
        foreach (var profile in profiles)
        {
            var selected = required.Concat(profile.Skip(1)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var desired = prepared.Where(pair => selected.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            await installer.ReconcileAsync(desired);
            var errors = await installer.VerifyAsync();
            if (errors.Count != 0) throw new Exception($"Profile {profile[0]} failed: " + string.Join("; ", errors.Take(10)));
            Console.WriteLine($"PROFILE PASS {profile[0]} modules={desired.Count}");
        }
    }
    finally
    {
        var full = Path.GetFullPath(root);
        var safeParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PawsPatchLauncherProfileTests")) + Path.DirectorySeparatorChar;
        if (full.StartsWith(safeParent, StringComparison.OrdinalIgnoreCase) && Directory.Exists(full)) Directory.Delete(full, true);
    }
}
