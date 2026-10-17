using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

// Real MainWindow commands, signed feeds and ZIP transactions in a disposable smoke profile.
// The fake executable is text and no Launch command is invoked. No native window is shown.
internal static class ModLibraryChecks
{
    internal static void Run(string language, bool modManager = false, bool legacyGame = false)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null)
            throw new InvalidOperationException("Mod library UI checks require a disposable --smoke-test profile.");
        var root = Path.Combine(ActivityStore.Root, "mod-library-ui", Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var sources = Path.Combine(root, "sources");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(game); Directory.CreateDirectory(sources);
        if (legacyGame)
        {
            // Kohan II has no version resource; the launcher reads its NUL-delimited UTF-16 version string.
            // 1.3.68 is the public Steam build; the feed's engine runtime below targets 1.3.72.
            File.WriteAllBytes(Path.Combine(game, "k2.exe"), Encoding.Unicode.GetBytes("Non-executable isolated UI fixture; never launch.\u00001.3.68\u0000"));
            Directory.CreateDirectory(Path.Combine(game, "startup"));
            File.WriteAllText(Path.Combine(game, "startup", "autoexec.txt"), "adddepot data.rwd\r\n");
        }
        else File.WriteAllText(Path.Combine(game, "k2.exe"), "Non-executable isolated UI fixture; never launch.");
        var settingsPath = Path.Combine(ActivityStore.Root, "settings.json");
        var oldSettings = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var windows = new List<MainWindow>();
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var blockedNetwork = new RejectNetwork();
        using var http = new HttpClient(blockedNetwork);
        var configuration = new LauncherConfiguration
        {
            FeedUrls = [Path.Combine(sources, "stable.json")], BetaFeedUrls = [], CacheRoot = cache,
            PublicKeyPem = signing.ExportSubjectPublicKeyInfoPem(), PreferredGameExecutable = "k2.exe"
        };
        var count = 0;
        void Check(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException("Mod library UI: " + why);
            count++;
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(MainWindow window, string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(MainWindow window, string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(MainWindow window, string name, params object?[] args)
            => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        T Control<T>(MainWindow window, string name) => (T)window.FindName(name);
        UserSettings Settings(MainWindow window) => (UserSettings)Field(window, "_settings")!;
        ChannelManifest Selected(MainWindow window) => (ChannelManifest)Field(window, "_channel")!;
        InstallState State() => new ModuleInstaller(game).LoadState();
        bool Flag(MainWindow window, string name) => (bool)Field(window, name)!;
        string StateText() => File.ReadAllText(Path.Combine(game, ".pawpatch", "state.json"));

        MainWindow Window()
        {
            var window = new MainWindow(configuration, new FeedClient(configuration, http))
            {
                Width = 1050, Height = 680, Left = -32000, Top = -32000,
                ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
            };
            windows.Add(window);
            Set(window, "_game", new GameInstallation(game, Path.Combine(game, "k2.exe"), "fixture", "stable"));
            // A user's open game must neither block nor be inspected by this isolated fixture.
            Set(window, "_gameRunningProbe", new Func<bool>(() => false));
            var userData = Path.Combine(root, "documents", "Kohan2", "data");
            Set(window, "_userDataDirectory", new Func<string>(() => userData));
            Call(window, "ApplyLanguage");
            Call(window, "SetActivePage", "modules");
            window.Measure(new Size(1050, 680)); window.Arrange(new Rect(0, 0, 1050, 680)); window.UpdateLayout();
            return window;
        }
        async Task<bool> CheckFeed(MainWindow window) => await (Task<bool>)Call(window, "CheckFeedAsync", false)!;
        void Select(MainWindow window, string mod)
        {
            var name = mod == GameMod.Immortals ? "ImmortalsModRadio" : "VanillaModRadio";
            var selector = Control<RadioButton>(window, name);
            Check(selector.IsEnabled, mod + " selector is disabled.");
            selector.IsChecked = true;
            Check(Settings(window).Mod == mod, "Mod selection did not reach saved preferences.");
        }
        void InstallOnly(MainWindow window)
        {
            var update = Control<Button>(window, "UpdateButton");
            var apply = Control<Button>(window, "ApplySettingsButton");
            Check(!Flag(window, "_patchInstalled") && update.IsEnabled, "An absent mod has no usable Install action.");
            Check(Equals(update.Content, ((PawsPatchLauncher.Localization)Field(window, "_text")!)["button.install"]), "An absent mod is presented as Update or Apply.");
            Check(!apply.IsEnabled && !apply.IsHitTestVisible && !apply.IsTabStop, "An absent mod can be applied before installation.");
            Check(!Control<Button>(window, "LaunchButton").IsEnabled, "An absent mod can be launched.");
            Check(!Flag(window, "_patchUpdateAvailable"), "A first installation is reported as an update.");
        }
        async Task ApplyOnly(MainWindow window)
        {
            await (Task<bool>)Call(window, "CheckGameCompatibilityAsync", true)!;
            var apply = Control<Button>(window, "ApplySettingsButton");
            Check(Flag(window, "_patchInstalled") && Flag(window, "_settingsPending"), "Stored inactive mod is not pending Apply.");
            Check(apply.IsEnabled && apply.IsHitTestVisible && apply.IsTabStop && apply.Visibility == Visibility.Visible, "Apply is not visible and operable.");
            Check(apply.Background is SolidColorBrush actual && window.FindResource("GoldBrush") is SolidColorBrush expected && actual.Color == expected.Color,
                "Apply is not rendered with the primary gold color.");
            Check(!Control<Button>(window, "LaunchButton").IsEnabled, "Launch is enabled before applying the selected configuration.");
            Check(!Control<Button>(window, "UpdateButton").IsEnabled && !Flag(window, "_patchUpdateAvailable"), "Switching a stored mod offers a download/update.");
        }
        void Ready(MainWindow window, string mod)
        {
            Check(State().AppliedSettings?.Mod == mod && Settings(window).Mod == mod, "Applied mode and selection disagree.");
            Check(Flag(window, "_patchInstalled") && !Flag(window, "_settingsPending"), "Completed configuration is still pending.");
            Check(Control<Button>(window, "LaunchButton").IsEnabled, "Completed configuration cannot launch.");
            Check(!Control<Button>(window, "ApplySettingsButton").IsHitTestVisible, "Completed configuration still exposes Apply.");
            Check(Field(window, "_installationFailure") is null, "Installation failure survived a completed transaction.");
        }
        async Task CheckInactiveCompatibility(MainWindow window, ChannelManifest compatibleOffer)
        {
            var fields = new[] { "_compatibilityState", "_lastProbeDataOnly", "_latestChannel", "_compatiblePatchUpdate", "_compatibilityNoticeKey" };
            var previous = fields.ToDictionary(name => name, name => Field(window, name));
            var readyText = Control<TextBlock>(window, "ReadyStatusText").Text;
            var stateText = StateText();
            try
            {
                Set(window, "_compatibilityState", GameCompatibilityState.Unsupported);
                Set(window, "_lastProbeDataOnly", true);
                Set(window, "_latestChannel", compatibleOffer);
                // Also model a candidate left over from viewing Immortals before selecting Vanilla.
                Set(window, "_compatiblePatchUpdate", compatibleOffer);
                Set(window, "_compatibilityNoticeKey", "unread-immortals-compatibility-fixture");
                await (Task<bool>)Call(window, "CheckGameCompatibilityAsync", true)!;
                Call(window, "RenderCompatibility");
                Check(Control<Button>(window, "GameCompatibilityButton").Visibility == Visibility.Collapsed
                    && Control<TextBlock>(window, "GameCompatibilityHint").Visibility == Visibility.Collapsed,
                    "An Immortals compatibility warning is shown for active Vanilla.");
                Check(Control<Border>(window, "CompatibilityBanner").Visibility == Visibility.Collapsed
                    && Control<Border>(window, "ModulesNavBadge").Visibility == Visibility.Collapsed,
                    "Immortals compatibility creates a Vanilla component banner or unread badge.");
                Check(Field(window, "_compatibilityPopup") is null && Field(window, "_compatibilityBackdrop") is null,
                    "Immortals compatibility opened a patch-update modal for Vanilla.");
                Check(Control<TextBlock>(window, "ReadyStatusText").Text == readyText && Control<Button>(window, "LaunchButton").IsEnabled,
                    "Immortals compatibility replaced Vanilla readiness with a patch-update requirement.");
                Check(Control<Grid>(window, "MainBody").IsHitTestVisible && StateText() == stateText,
                    "Immortals compatibility blocked Vanilla UI or changed installed state.");
            }
            finally
            {
                foreach (var (name, value) in previous) Set(window, name, value);
                Call(window, "RefreshStatus");
            }
        }
        async Task Install(MainWindow window)
        {
            Check(Control<Button>(window, "UpdateButton").IsEnabled, "Install action is disabled.");
            Control<Button>(window, "UpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while (Flag(window, "_busy") && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
            Check(!Flag(window, "_busy"), "Install did not finish.");
            Ready(window, Settings(window).Mod);
        }
        async Task Apply(MainWindow window)
        {
            Check(Control<Button>(window, "ApplySettingsButton").IsEnabled, "Apply action is disabled.");
            await (Task)Call(window, "ApplySettingsAsync")!;
            Ready(window, Settings(window).Mod);
        }
        async Task CheckFiles(MainWindow window)
        {
            await (Task)Call(window, "CheckReadinessAsync")!;
            Check(Field(window, "_readiness") is ReadinessReport { Errors.Count: 0 },
                "File verification rejected a fully applied mode.");
        }
        void ChangeLanguage(MainWindow window, string code)
        {
            var combo = Control<ComboBox>(window, "GameLanguageCombo");
            combo.SelectedItem = combo.Items.Cast<object>().Single(item => (string?)item.GetType().GetProperty("Code")!.GetValue(item) == code);
            Check(Settings(window).RussianLocalization == (code == "ru"), "Game language choice did not reach preferences.");
        }
        void RemoveDownloadSources()
        {
            // All deletions are individual files under this newly-created fixture, never game/profile roots.
            foreach (var file in Directory.GetFiles(sources, "*", SearchOption.AllDirectories)) File.Delete(file);
            var downloads = Path.Combine(cache, "downloads");
            if (Directory.Exists(downloads))
                foreach (var file in Directory.GetFiles(downloads, "*", SearchOption.AllDirectories)) File.Delete(file);
        }
        async Task<PackageRelease> Package(string id, string version, int priority, Dictionary<string, string> files, bool required = false)
        {
            var archive = Path.Combine(sources, id + "-" + version + ".zip");
            var manifest = new ModuleArchiveManifest { Id = id, Version = version };
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                foreach (var (name, body) in files)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    manifest.Files.Add(new() { Path = name, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                    using var output = zip.CreateEntry("payload/" + name).Open(); output.Write(bytes);
                }
                using var writer = new StreamWriter(zip.CreateEntry("module.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.ModuleArchiveManifest));
            }
            return new() { Id = id, Version = version, Priority = priority, Required = required, ExecutableIndependent = true,
                Name = new() { Ru = id, En = id }, Size = new FileInfo(archive).Length, Sha256 = await CryptoAndIO.Sha256Async(archive), Urls = [archive] };
        }
        async Task WriteFeed(ChannelManifest channel)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(channel, LauncherJsonContext.Default.ChannelManifest);
            var signed = new SignedFeedEnvelope { KeyId = "disposable-ui-test", Payload = Convert.ToBase64String(payload),
                Signature = Convert.ToBase64String(signing.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            await File.WriteAllTextAsync(configuration.FeedUrls[0], JsonSerializer.Serialize(signed, LauncherJsonContext.Default.SignedFeedEnvelope));
        }

        async Task<(ChannelManifest Feed, string Exe)> InitialFeed()
        {
            var originalExe = await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe"));
            var initial = new ChannelManifest { PublishedAt = "2026-09-10T10:00:00Z", Game = legacyGame
                ? new() { Version = "1.3.72", SteamBuild = "25068126", K2ExeSha256 = ["1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45"] }
                : new() { K2ExeSha256 = [originalExe] } };
            // Paw's Patch for Vanilla and Immortals: a file part and an engine part that only runs on 1.3.72.
            var pureData = await Package("pure-fixes-data", "1.0.0", 50, new() { ["data/pure.txt"] = "PURE" });
            var pureRuntime = await Package("pure-fixes-runtime", "1.0.0", 60, new() { ["k2_paws_pure_fixes_1372.exe"] = "Engine fixes for 1.3.72; never launch." });
            pureRuntime.ExecutableIndependent = false;
            pureData.Mods = pureRuntime.Mods = [GameMod.Vanilla, GameMod.Immortals];
            initial.Packages.AddRange([pureData, pureRuntime]);
            initial.Packages.Add(await Package("startup-base", "1.0.0", 20, new() { ["startup/base.txt"] = "base" }, true));
            initial.Packages.Add(await Package("game-localization-en", "1.0.0", 90, new() { ["startup/language.txt"] = "en" }));
            initial.Packages.Add(await Package("vanilla-localization-ru", "1.0.0", 100, new() { ["startup/language.txt"] = "ru" }));
            initial.Packages.Add(await Package("immortals", "2.1.0", 10, new() { ["data/shared.txt"] = "IMM", ["data/imm.txt"] = "Immortals" }));
            initial.Packages.Add(await Package("immortals-localization-ru", "1.0.0", 100, new() { ["startup/language.txt"] = "ru" }));
            await WriteFeed(initial);
            new SettingsStore().Save(new UserSettings { Language = language, Mod = GameMod.Vanilla, ModNoticeSeen = true, GamePath = game,
                Channel = "stable", PawPatchEnabled = true, VanillaPawPatchEnabled = true, ImmortalsPawPatchEnabled = legacyGame,
                LargeMapSizes = false, RussianLocalization = true, IndependentHostility = false,
                RoamingSpawnMode = "standard", AdditionalRoamingCompanies = false, SiegeBalance = false, DisablePowersAndShards = false });
            return (initial, originalExe);
        }

        async Task Scenario()
        {
            var (initial, originalExe) = await InitialFeed();
            var initialRelease = ChannelFingerprint.Create(initial);
            var window = Window();
            Check(await CheckFeed(window), "Signed local feed was not accepted.");
            InstallOnly(window);
            await Install(window);
            Check(ModLibrary.Packages(initial, GameMod.Vanilla).All(new ModuleInstaller(game).IsPrepared), "Install omitted Paw's Patch or a game language.");
            Check(File.Exists(Path.Combine(game, "data/pure.txt")) && !File.Exists(Path.Combine(game, "data/imm.txt")), "Vanilla with Paw's Patch was not applied.");
            var stateBeforeSelection = StateText();
            Select(window, GameMod.Immortals);
            InstallOnly(window);
            Check(StateText() == stateBeforeSelection, "Selecting an uninstalled mod changed game files/state.");
            await Install(window);
            Check(new ModLibrary(game).Find(GameMod.Vanilla, "stable")?.ReleaseId == initialRelease, "Installing Immortals discarded the stored Vanilla release.");
            Check(new ModLibrary(game).Find(GameMod.Immortals, "stable")?.Packages.Single(p => p.Id == "immortals").Version == "2.1.0", "Immortals was not retained with its installed version.");

            // Model languages selected in earlier sessions. Initial installation
            // intentionally downloads only its chosen language (covered separately).
            foreach (var package in initial.Packages.Where(GameLanguages.IsLanguage))
                await new ModuleInstaller(game).PrepareAsync(package, package.Urls[0]);

            RemoveDownloadSources();
            Check(!await CheckFeed(window), "Missing local sources were treated as an online check.");
            Ready(window, GameMod.Immortals);
            stateBeforeSelection = StateText();
            Select(window, GameMod.Vanilla);
            await ApplyOnly(window);
            Check(StateText() == stateBeforeSelection && File.Exists(Path.Combine(game, "data/imm.txt")), "A tab switch applied the stored Vanilla release without confirmation.");
            await Apply(window);
            Check(File.Exists(Path.Combine(game, "data/pure.txt")) && !File.Exists(Path.Combine(game, "data/imm.txt")) && !File.Exists(Path.Combine(game, "data/shared.txt")),
                "Offline Vanilla switch retained Immortals files.");
            Check(!Control<Button>(window, "UpdateButton").IsEnabled, "An offline switch enabled a download.");
            var burstState = StateText();
            var clickTimes = new List<double>();
            for (var i = 0; i < 45; i++)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew(); Select(window, i % 2 == 0 ? GameMod.Immortals : GameMod.Vanilla); window.UpdateLayout();
                clickTimes.Add(timer.Elapsed.TotalMilliseconds);
            }
            Check(StateText() == burstState && Settings(window).Mod == GameMod.Immortals, "Rapid offline selection changed the active installation.");
            Check(clickTimes.Max() < 500, "An offline mode click stalled for half a second.");
            Console.WriteLine($"OFFLINE MODE PERFORMANCE {language}: 45 clicks; max={clickTimes.Max():F1}ms; mean={clickTimes.Average():F1}ms; no applied writes");
            Select(window, GameMod.Vanilla);
            var coreToggle = Control<CheckBox>(window, "PawPatchToggle");
            Check(coreToggle.IsEnabled && coreToggle.IsChecked == true, "Paw's Patch toggle is unavailable for stored Vanilla.");
            coreToggle.IsChecked = false; coreToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!GameMod.PawPatchSelected(Settings(window)), "Paw's Patch toggle did not change the selected configuration.");
            await ApplyOnly(window); await Apply(window);
            Check(!File.Exists(Path.Combine(game, "data/pure.txt")), "Turning Paw's Patch off offline left its files.");
            coreToggle.IsChecked = true; coreToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            await ApplyOnly(window); await Apply(window);
            Check(File.ReadAllText(Path.Combine(game, "data/pure.txt")) == "PURE", "Paw's Patch could not be applied again offline.");
            ChangeLanguage(window, "en");
            Check(GameLanguages.Voice(Settings(window)) == "en", "Legacy combined translation kept an incompatible speech choice.");
            await ApplyOnly(window); await Apply(window);
            Check(File.ReadAllText(Path.Combine(game, "startup/language.txt")) == "en", "Offline English selection was not applied.");
            await CheckFiles(window);
            ChangeLanguage(window, "ru");
            await ApplyOnly(window); await Apply(window);
            Check(File.ReadAllText(Path.Combine(game, "startup/language.txt")) == "ru", "Offline Russian selection was not applied.");
            Select(window, GameMod.Immortals);
            await ApplyOnly(window); await Apply(window);
            Check(Settings(window).RussianLocalization && File.ReadAllText(Path.Combine(game, "startup/language.txt")) == "ru", "Immortals did not preserve the chosen language.");
            await CheckFiles(window);
            Select(window, GameMod.Vanilla);
            await ApplyOnly(window); await Apply(window);

            var offered = JsonSerializer.Deserialize(JsonSerializer.Serialize(initial, LauncherJsonContext.Default.ChannelManifest), LauncherJsonContext.Default.ChannelManifest)!;
            offered.PublishedAt = "2026-09-11T10:00:00Z";
            offered.Packages.RemoveAll(package => package.Id == "immortals");
            offered.Packages.Add(await Package("immortals", "2.2.0", 10, new() { ["data/shared.txt"] = "IMM updated", ["data/imm.txt"] = "Immortals" }));
            await WriteFeed(offered);
            Check(await CheckFeed(window), "Immortals-only update feed was not accepted.");
            Ready(window, GameMod.Vanilla);
            await CheckInactiveCompatibility(window, offered);
            Check(!Flag(window, "_patchUpdateAvailable") && !Control<Button>(window, "UpdateButton").IsEnabled, "Immortals-only update was offered to active Vanilla.");
            stateBeforeSelection = StateText();
            Select(window, GameMod.Immortals);
            await ApplyOnly(window);
            Check(StateText() == stateBeforeSelection, "Selecting Immortals for an offered update modified the active Vanilla install.");
            Check(Selected(window).Packages.Single(package => package.Id == "immortals").Version == "2.1.0", "Selecting stored Immortals substituted the remote release before Apply.");
            await Apply(window);
            Check(Flag(window, "_patchUpdateAvailable") && Control<Button>(window, "UpdateButton").IsEnabled, "Immortals update was not offered after Immortals became active.");
            Check(State().ReleaseId == initialRelease && new ModLibrary(game).Find(GameMod.Immortals, "stable")?.ReleaseId == initialRelease,
                "Apply silently upgraded the stored release while a newer feed was available.");

            RemoveDownloadSources();
            window.Close();
            var reopened = Window(); // Fresh MainWindow, FeedClient and SettingsStore: no in-memory release survives.
            Check(Settings(reopened).Mod == GameMod.Immortals && Settings(reopened).RussianLocalization,
                "Restart lost the selected mod or common language.");
            Check(!await CheckFeed(reopened), "Offline restart unexpectedly reached a feed source.");
            Ready(reopened, GameMod.Immortals);
            Check(Selected(reopened).Packages.Single(package => package.Id == "immortals").Version == "2.1.0"
                && ChannelFingerprint.Create(Selected(reopened)) == initialRelease, "Offline restart loaded the offered version instead of the installed release.");
            Select(reopened, GameMod.Vanilla);
            await ApplyOnly(reopened); await Apply(reopened);
            Check(File.ReadAllText(Path.Combine(game, "data/pure.txt")) == "PURE" && !File.Exists(Path.Combine(game, "data/imm.txt")),
                "Offline restart could not restore the inactive installed Vanilla release.");
            Check(!Flag(reopened, "_patchUpdateAvailable"), "Cached Immortals-only update leaked into Vanilla after restart.");
            Check(new ModLibrary(game).Load().Mods.Select(entry => entry.Mod).Distinct().Count() == 2, "Stored mod entries changed.");
            Check(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")) == originalExe, "UI test altered the base executable.");
            Check((await new ModuleInstaller(game).VerifyAsync()).Count == 0, "Final active game files do not match their signed package payloads.");
            Check(blockedNetwork.Attempts == 0, "Fixture attempted an external network request.");
            Console.WriteLine($"MOD LIBRARY UI PASS {count} {language}: install-only, gold Apply, launch lock, offline patch/language switching, active-mod updates, fresh-window installed release; signed local ZIPs; no native window/game/network.");
        }

        // Mod manager Game rows: one click selects the game and runs the same install or apply as Components.
        Button? GameButton(MainWindow window, string mod) => ((List<(string Mod, Button Button, bool Allowed)>)Field(window, "_baseGameButtons")!)
            .Where(row => row.Mod == mod).Select(row => row.Button).FirstOrDefault();
        long? DownloadSize(MainWindow window, string mod) => (long?)Call(window, "BaseGameDownloadSize", mod);
        async Task OneClick(MainWindow window, string mod)
        {
            var button = GameButton(window, mod);
            Check(button is { IsEnabled: true }, mod + " has no usable Game row action.");
            button!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while ((Flag(window, "_busy") || Flag(window, "_baseGameSwitching")) && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
            Check(!Flag(window, "_busy") && !Flag(window, "_baseGameSwitching"), mod + " Game row action did not finish.");
            Ready(window, mod);
            Check((string)Field(window, "_activePage")! == "modmanager" && Control<StackPanel>(window, "ModManagerPanel").Visibility == Visibility.Visible,
                "A Game row action left the Mod manager.");
            Check(Control<Border>(window, "CoreModuleCard").Visibility != Visibility.Visible && Control<Border>(window, "SiegeBalanceCard").Visibility != Visibility.Visible,
                "A Game row action revealed Components cards on the Mod manager page.");
            Check(GameButton(window, mod) is null, "The active game still offers an action.");
        }
        async Task ModManagerScenario()
        {
            var (initial, originalExe) = await InitialFeed();
            var window = Window();
            Check(await CheckFeed(window), "Signed local feed was not accepted.");
            Call(window, "SetActivePage", "modmanager"); window.UpdateLayout();
            var text = (PawsPatchLauncher.Localization)Field(window, "_text")!;
            var switchLabel = (string)Call(window, "T", "Переключить", "Switch")!;
            Check(LauncherIcon.GetKind(Control<Button>(window, "ModManagerNav")) == IconKind.Mods, "Mod manager navigation has no icon.");
            Check(Control<StackPanel>(window, "ModManagerGamesBody").Children.Count == 2, "Game card does not list Vanilla and Immortals.");
            var immortalsBytes = ModLibrary.Packages(initial, GameMod.Immortals).Sum(p => p.Size);
            Check(immortalsBytes > 0 && DownloadSize(window, GameMod.Immortals) == immortalsBytes, "Immortals download size is wrong.");
            Check(Equals(GameButton(window, GameMod.Immortals)?.Content, text["button.install"]), "A game that is not stored is not offered as Install.");
            await OneClick(window, GameMod.Immortals);
            Check(new ModLibrary(game).Find(GameMod.Immortals, "stable") is not null && File.ReadAllText(Path.Combine(game, "data/shared.txt")) == "IMM",
                "One click did not download, store and apply Immortals.");
            await OneClick(window, GameMod.Vanilla);
            Check(File.Exists(Path.Combine(game, "data/pure.txt")) && !File.Exists(Path.Combine(game, "data/imm.txt")) && new ModLibrary(game).Find(GameMod.Immortals, "stable") is not null,
                "Installing Vanilla failed or dropped the stored Immortals release.");
            Check(DownloadSize(window, GameMod.Immortals) == 0 && Equals(GameButton(window, GameMod.Immortals)?.Content, switchLabel),
                "Stored Immortals is not offered as a switch.");
            RemoveDownloadSources(); // A stored game must switch with nothing left to download.
            await OneClick(window, GameMod.Immortals);
            Check(File.ReadAllText(Path.Combine(game, "data/shared.txt")) == "IMM", "Switching to stored Immortals offline failed.");
            var stateText = StateText();
            Set(window, "_customModsDirty", true);
            GameButton(window, GameMod.Vanilla)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(StateText() == stateText && Settings(window).Mod == GameMod.Immortals && Field(window, "_customModsError") is string,
                "Unapplied custom mod edits did not block a game switch.");
            Set(window, "_customModsDirty", false); Call(window, "ReloadCustomMods");
            Set(window, "_busy", true); Call(window, "RefreshCustomModsState", false);
            Check(!GameButton(window, GameMod.Vanilla)!.IsEnabled, "A Game row action is enabled during another operation.");
            Set(window, "_busy", false); Call(window, "RefreshCustomModsState", false);
            Check(GameButton(window, GameMod.Vanilla)!.IsEnabled, "Game row actions stay disabled after the operation.");
            Check(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")) == originalExe, "UI test altered the base executable.");
            Check((await new ModuleInstaller(game).VerifyAsync()).Count == 0, "Final active game files do not match their signed package payloads.");
            Check(blockedNetwork.Attempts == 0, "Fixture attempted an external network request.");
            Console.WriteLine($"MOD MANAGER GAMES PASS {count} {language}: nav icon, two Game rows, one-click download+install of Immortals, offline switch of a stored game, dirty and busy guards; signed local ZIPs; no native window/game/network.");
        }

        IEnumerable<object> Logical(object node)
        {
            yield return node;
            if (node is DependencyObject parent)
                foreach (var child in LogicalTreeHelper.GetChildren(parent))
                    foreach (var item in Logical(child)) yield return item;
        }
        string GameRowsText(MainWindow window) => string.Join("\n", Logical(Control<StackPanel>(window, "ModManagerGamesBody")).OfType<TextBlock>().Select(t => t.Text));
        string Startup() => File.ReadAllText(Path.Combine(game, "startup", "autoexec.txt"));
        // What Play checks before it starts the game, without starting it.
        async Task PlayReady(MainWindow window, string why)
        {
            var state = State();
            var exe = (string)Call(window, "ResolveLaunchExecutable", game)!;
            var selected = (List<PackageRelease>)Call(window, "ResolveSelectedPackages", Selected(window))!;
            Check(Path.GetFileName(exe) == "k2.exe" && File.Exists(exe), why + ": Play would not start the game's own k2.exe.");
            Check(!UpdateDetector.HasSettingsChanges(state, selected, (UserSettings)Call(window, "GetEffectiveSettings")!), why + ": Play would ask to apply settings again.");
            var problems = (await MultiplayerCheck.CriticalAsync(game, state, exe, new GameRequirement()))
                .Concat(await new ModuleInstaller(game).VerifyAsync(allowCustomHotkeys: true)).ToList();
            Check(problems.Count == 0, why + ": Play's file check fails: " + string.Join("; ", problems));
        }
        // The player's setup on the public 1.3.68 build: Vanilla, Paw's Patch off, a custom RWD on, the sandbox on. The fixture
        // executable is text, so the real 1.3.68 hash is set the way the headless checks do. Captures both pages.
        async Task PlayerStateScenario(MainWindow window)
        {
            Call(window, "SetActivePage", "modules"); window.UpdateLayout();
            if (GameMod.PawPatchSelected(Settings(window)))
            {
                // What `--headless set paw=off` + `apply` did on the player's install.
                GameMod.SetPawPatch(Settings(window), false);
                await (Task<bool>)Call(window, "CheckGameCompatibilityAsync", true)!;
                Call(window, "RefreshStatus");
                await (Task)Call(window, "ApplySettingsAsync")!;
            }
            Check(!GameMod.PawPatchSelected(Settings(window)) && State().AppliedSettings is { Mod: GameMod.Vanilla } applied && !GameMod.PawPatchSelected(applied),
                "Vanilla without Paw's Patch was not applied.");
            const string publicExe = "CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A";
            Set(window, "_gameHash", publicExe); Set(window, "_sandboxExeSha256", publicExe);
            Settings(window).SandboxEnabled = true;
            // Off-screen (-32000) and not activated, like the other layout checks: visibility and capture need a laid-out window.
            window.Show();
            Call(window, "RenderCompatibility"); Call(window, "RefreshStatus"); Call(window, "RefreshModControls");
            // A popup left open by the fixture's earlier unknown executable and the earlier toasts are not part of this state.
            Call(window, "CloseCompatibilityPopup"); Call(window, "ClearToastStack");
            window.UpdateLayout();
            bool Visible(string name) => window.FindName(name) is FrameworkElement { IsVisible: true };
            string Shown(string name) => window.FindName(name) is FrameworkElement { IsVisible: true } element
                ? element switch { TextBlock t => t.Text, _ => "(visible)" } : "";
            var shown = new[] { "CompatibilityBanner", "GameCompatibilityHint", "GameCompatibilityButton", "PawCompatibilityButton", "CoreDescriptionText", "ReadyStatusText" }
                .Select(name => name + "=" + Shown(name)).ToArray();
            Console.WriteLine($"PLAYER STATE {language} components: " + string.Join(" | ", shown));
            Check(Field(window, "_compatibilityPopup") is null, "The 1.3.68 public build opened the compatibility popup.");
            Check(!Visible("CompatibilityBanner") && !Visible("GameCompatibilityHint") && !Visible("GameCompatibilityButton") && !Visible("PawCompatibilityButton")
                && !Visible("ModulesNavBadge"), "Components shows the file-only warning for the 1.3.68 public build.");
            Check(!Control<TextBlock>(window, "CoreDescriptionText").Text.Contains((string)Call(window, "T", "в красном значке", "red icon")!),
                "The core card still points at the red icon.");
            Check(Control<TextBlock>(window, "ReadyStatusText").Text == GameMod.Name(GameMod.Vanilla, language == "ru") + (string)Call(window, "T", " · Готово к запуску", " · Ready to play")!,
                "The player's setup is not ready to play.");
            Check(!Visible("SandboxCard"), "The sandbox switch is still on the Components page.");
            await Task.Delay(600); // page and navigation transitions
            CapturePage(window, "player-components");
            Call(window, "SetActivePage", "modmanager"); window.UpdateLayout();
            var sandboxToggle = Control<CheckBox>(window, "SandboxToggle");
            Console.WriteLine($"PLAYER STATE {language} mod manager: sandbox row visible={Visible("SandboxCard")} checked={sandboxToggle.IsChecked} enabled={sandboxToggle.IsEnabled}");
            Check(Visible("SandboxCard") && sandboxToggle.IsChecked == true && sandboxToggle.IsEnabled, "The sandbox entry is missing from the Mod manager list or not switchable.");
            Check(Equals(Control<Button>(window, "LaunchButton").Content, Call(window, "T", "Играть в песочнице", "Play sandbox")), "Launch does not read Play sandbox.");
            sandboxToggle.IsChecked = false; sandboxToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(!Settings(window).SandboxEnabled && !new SettingsStore().Load().SandboxEnabled, "Turning the sandbox entry off was not saved.");
            sandboxToggle.IsChecked = true; sandboxToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(Settings(window).SandboxEnabled, "Turning the sandbox entry on was not saved.");
            await Task.Delay(600); // page and navigation transitions
            Control<Border>(window, "SandboxCard").BringIntoView(); window.UpdateLayout(); await Task.Delay(100);
            CapturePage(window, "player-modmanager");
        }
        void CapturePage(MainWindow window, string name)
        {
            var view = (FrameworkElement)window.Content; view.UpdateLayout();
            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(view);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            var path = Path.Combine(Path.GetTempPath(), "k2-player-state", name + "-" + language + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using (var stream = File.Create(path)) encoder.Save(stream);
            Console.WriteLine("PLAYER STATE capture: " + path);
        }
        // The public Steam build 1.3.68: Paw's engine runtime targets 1.3.72, so each game installs and plays with file changes only.
        async Task ModManagerLegacyGameScenario()
        {
            var (_, originalExe) = await InitialFeed();
            var window = Window();
            Check(await CheckFeed(window), "Signed local feed was not accepted.");
            await (Task<bool>)Call(window, "CheckGameCompatibilityAsync", true)!;
            Check((GameCompatibilityState)Field(window, "_compatibilityState")! == GameCompatibilityState.Unsupported && (string)Field(window, "_installedGameVersion")! == "1.3.68",
                "The 1.3.68 executable was not read as a file-only game build.");
            Call(window, "SetActivePage", "modmanager"); window.UpdateLayout();
            var fileOnly = (string)Call(window, "T", "Только файловые изменения", "File changes only")!;
            async Task FileOnly(string mod, string marker, string why)
            {
                await OneClick(window, mod);
                Check(State().AppliedSettings?.DataOnly == true && File.Exists(Path.Combine(game, marker)), why + ": the game's files were not applied in file-only mode.");
                Check(!File.Exists(Path.Combine(game, "k2_paws_pure_fixes_1372.exe")), why + ": 1.3.72 engine files were installed on 1.3.68.");
                Check(GameRowsText(window).Contains(fileOnly + " · Kohan II 1.3.68"), why + ": the active row does not say it runs with file changes only.");
                await PlayReady(window, why);
            }
            await FileOnly(GameMod.Vanilla, "data/pure.txt", "Vanilla with Paw's Patch");
            Check(new ModuleInstaller(game).IsPrepared(Selected(window).Packages.Single(p => p.Id == "pure-fixes-runtime")),
                "The engine part was not stored for a later switch to 1.3.72.");

            // A custom RWD mod mounts through the startup file, which 1.3.68 reads like 1.3.72.
            var rwd = Path.Combine(root, "Battle mod.rwd");
            PawsPatchLauncher.Rwd.RwdArchive.Write(rwd, new Dictionary<string, byte[]> { ["test.tgi"] = [1] });
            Set(window, "_chooseRwdMod", (Func<string?>)(() => rwd));
            async Task CustomIdle()
            {
                for (var i = 0; i < 200 && Flag(window, "_customModsBusy"); i++) await Task.Delay(20);
                Check(!Flag(window, "_customModsBusy"), "A custom mod action did not finish.");
                window.UpdateLayout();
            }
            Control<Button>(window, "CustomModsAdd").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await CustomIdle();
            Call(window, "ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Control<Button>(window, "ModManagerApply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await CustomIdle();
            const string mountHeader = "# Paw's Launcher: local RWD mods";
            Check(State().CustomMods.GetValueOrDefault(GameMod.Vanilla)?.SingleOrDefault()?.Enabled == true && Startup().StartsWith("adddepot data.rwd") && Startup().Contains(mountHeader),
                "The custom mod was not mounted after the game's own data on 1.3.68.");
            Check(State().AppliedSettings?.DataOnly == true && !File.Exists(Path.Combine(game, "k2_paws_pure_fixes_1372.exe")), "Applying a custom mod left file-only mode.");
            await PlayReady(window, "Vanilla with a custom mod");

            await FileOnly(GameMod.Immortals, "data/imm.txt", "Immortals");
            Check(!Startup().Contains(mountHeader), "Vanilla's custom mod was mounted for Immortals.");
            RemoveDownloadSources(); // Back to the stored Vanilla, offline.
            await FileOnly(GameMod.Vanilla, "data/pure.txt", "Back to Vanilla");
            Check(Startup().Contains(mountHeader), "Vanilla's custom mod was not mounted again after switching back.");
            await PlayerStateScenario(window);
            Check(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")) == originalExe, "UI test altered the base executable.");
            Check(blockedNetwork.Attempts == 0, "Fixture attempted an external network request.");
            Console.WriteLine($"MOD MANAGER 1.3.68 PASS {count} {language}: file-only install of Vanilla with Paw's Patch and Immortals from the Mod manager, engine part stored but not installed, custom RWD mount, Play checks with k2.exe, offline switch back; no native window/game/network.");
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            Func<Task> scenario = !modManager ? Scenario : legacyGame ? ModManagerLegacyGameScenario : ModManagerScenario;
            var task = dispatcher.InvokeAsync(scenario).Task.Unwrap();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
            timer.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            if (!task.IsCompleted) throw new TimeoutException("Mod library UI checks did not complete.");
            task.GetAwaiter().GetResult();
        }
        finally
        {
            foreach (var window in windows) { Set(window, "_busy", false); window.Close(); }
            if (oldSettings is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, oldSettings);
        }
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Attempts);
            throw new InvalidOperationException("External network is forbidden in the local mod-library UI fixture.");
        }
    }
}
