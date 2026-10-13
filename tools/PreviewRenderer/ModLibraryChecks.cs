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
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null)
            throw new InvalidOperationException("Mod library UI checks require a disposable --smoke-test profile.");
        var root = Path.Combine(ActivityStore.Root, "mod-library-ui", Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var sources = Path.Combine(root, "sources");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(game); Directory.CreateDirectory(sources);
        File.WriteAllText(Path.Combine(game, "k2.exe"), "Non-executable isolated UI fixture; never launch.");
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
            FixtureAccess.AllowArcaneWars(window);
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
            var name = mod == GameMod.ArcaneWars ? "ArcaneWarsModRadio" : mod == GameMod.Immortals ? "ImmortalsModRadio" : "VanillaModRadio";
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
        void Connection(MainWindow window, bool online)
        {
            var account = (AccountService)Field(window, "_account")!;
            typeof(AccountService).GetProperty("State")!.SetValue(account, online ? AccountState.SignedIn : AccountState.Offline);
            var connection = typeof(AccountService).GetProperty("Connection", flags)!.GetValue(account)!;
            var request = connection.GetType().GetMethod("Begin", flags)!.Invoke(connection, null);
            connection.GetType().GetMethod("Complete", flags)!.Invoke(connection, [request, online]);
            Call(window, "RenderAccount"); Call(window, "RefreshStatus");
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
                // Also model a candidate left over from viewing AW before selecting Immortals.
                Set(window, "_compatiblePatchUpdate", compatibleOffer);
                Set(window, "_compatibilityNoticeKey", "unread-aw-compatibility-fixture");
                await (Task<bool>)Call(window, "CheckGameCompatibilityAsync", true)!;
                Call(window, "RenderCompatibility");
                Check(Control<Button>(window, "GameCompatibilityButton").Visibility == Visibility.Collapsed
                    && Control<TextBlock>(window, "GameCompatibilityHint").Visibility == Visibility.Collapsed,
                    "An AW compatibility warning is shown for active Immortals.");
                Check(Control<Border>(window, "CompatibilityBanner").Visibility == Visibility.Collapsed
                    && Control<Border>(window, "ModulesNavBadge").Visibility == Visibility.Collapsed,
                    "AW compatibility creates an Immortals component banner or unread badge.");
                Check(Field(window, "_compatibilityPopup") is null && Field(window, "_compatibilityBackdrop") is null,
                    "AW compatibility opened a patch-update modal for Immortals.");
                Check(Control<TextBlock>(window, "ReadyStatusText").Text == readyText && Control<Button>(window, "LaunchButton").IsEnabled,
                    "AW compatibility replaced Immortals readiness with a patch-update requirement.");
                Check(Control<Grid>(window, "MainBody").IsHitTestVisible && StateText() == stateText,
                    "AW compatibility blocked Immortals UI or changed installed state.");
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
                "File verification rejected a fully applied mode without the Arcane Wars core.");
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

        async Task Scenario()
        {
            var originalExe = await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe"));
            var initial = new ChannelManifest { PublishedAt = "2026-09-10T10:00:00Z", Game = new() { K2ExeSha256 = [originalExe] } };
            initial.Packages.Add(await Package("arcane-wars", "0.82.0", 10, new() { ["data/shared.txt"] = "AW", ["data/aw.txt"] = "AW base" }, true));
            initial.Packages.Add(await Package("startup-base", "1.0.0", 20, new() { ["startup/base.txt"] = "base" }, true));
            initial.Packages.Add(await Package("pawpatch-core", "1.0.0", 30, new() { ["data/shared.txt"] = "PAW", ["data/cost.txt"] = "0.75" }, true));
            initial.Packages.Add(await Package("siege-balance-standard", "1.0.0", 45, new() { ["data/cost.txt"] = "1.00" }));
            initial.Packages.Add(await Package("roaming-profile-standard-no-new", "1.0.0", 40, new()));
            initial.Packages.Add(await Package("powers-shards-original", "1.0.0", 40, new()));
            initial.Packages.Add(await Package("localization-ru", "1.0.0", 110, new() { ["startup/language.txt"] = "ru" }));
            initial.Packages.Add(await Package("aw-siege-balance", "1.0.0", 40, new() { ["data/cost.txt"] = "0.75" }));
            initial.Packages.Add(await Package("game-localization-en", "1.0.0", 90, new() { ["startup/language.txt"] = "en" }));
            initial.Packages.Add(await Package("vanilla-localization-ru", "1.0.0", 100, new() { ["startup/language.txt"] = "ru" }));
            initial.Packages.Add(await Package("aw-localization-ru", "1.0.0", 110, new() { ["startup/language.txt"] = "ru" }));
            initial.Packages.Add(await Package("immortals", "2.1.0", 10, new() { ["data/shared.txt"] = "IMM", ["data/imm.txt"] = "Immortals" }));
            initial.Packages.Add(await Package("immortals-localization-ru", "1.0.0", 100, new() { ["startup/language.txt"] = "ru" }));
            await WriteFeed(initial);
            var initialRelease = ChannelFingerprint.Create(initial);
            new SettingsStore().Save(new UserSettings { Language = language, Mod = GameMod.ArcaneWars, ModNoticeSeen = true, GamePath = game,
                Channel = "stable", PawPatchEnabled = false, LargeMapSizes = false, RussianLocalization = true, IndependentHostility = false,
                RoamingSpawnMode = "standard", AdditionalRoamingCompanies = false, SiegeBalance = false, DisablePowersAndShards = false });
            var window = Window();
            Connection(window, false);
            Check(!Control<RadioButton>(window, "ArcaneWarsModRadio").IsEnabled, "Offline membership allowed an uninstalled mod.");
            Connection(window, true);
            Check(await CheckFeed(window), "Signed local feed was not accepted.");
            InstallOnly(window);
            await Install(window);
            Check(ModLibrary.Packages(initial, GameMod.ArcaneWars).All(new ModuleInstaller(game).IsPrepared), "Install omitted disabled AW components or a game language.");
            Check(!File.Exists(Path.Combine(game, "data/cost.txt")), "Installing the library activated a disabled component.");
            var stateBeforeSelection = StateText();
            Select(window, GameMod.Immortals);
            InstallOnly(window);
            Check(StateText() == stateBeforeSelection, "Selecting an uninstalled mod changed game files/state.");
            await Install(window);
            Check(new ModLibrary(game).Find(GameMod.ArcaneWars, "stable")?.ReleaseId == initialRelease, "Installing Immortals discarded the stored AW release.");
            Check(new ModLibrary(game).Find(GameMod.Immortals, "stable")?.Packages.Single(p => p.Id == "immortals").Version == "2.1.0", "Immortals was not retained with its installed version.");

            // Model languages selected in earlier sessions. Initial installation
            // intentionally downloads only its chosen language (covered separately).
            foreach (var package in initial.Packages.Where(GameLanguages.IsLanguage))
                await new ModuleInstaller(game).PrepareAsync(package, package.Urls[0]);

            RemoveDownloadSources();
            Connection(window, false);
            Check(!await CheckFeed(window), "Missing local sources were treated as an online check.");
            Ready(window, GameMod.Immortals);
            stateBeforeSelection = StateText();
            Select(window, GameMod.ArcaneWars);
            await ApplyOnly(window);
            Check(StateText() == stateBeforeSelection && File.Exists(Path.Combine(game, "data/imm.txt")), "A tab switch applied the stored AW release without confirmation.");
            await Apply(window);
            Check(File.ReadAllText(Path.Combine(game, "data/shared.txt")) == "AW" && !File.Exists(Path.Combine(game, "data/imm.txt")), "Offline AW switch retained Immortals files.");
            Check(!Control<Button>(window, "UpdateButton").IsEnabled, "Offline local entitlement enabled a download.");
            try { Call(window, "EnsureModDownloadAccess", GameMod.ArcaneWars); throw new Exception("Offline download guard bypassed."); }
            catch (TargetInvocationException error) { Check(error.InnerException is InvalidOperationException, "Wrong offline download rejection."); }
            var burstState = StateText();
            var clickTimes = new List<double>();
            for (var i = 0; i < 15; i++)
            foreach (var mod in new[] { GameMod.Immortals, GameMod.Vanilla, GameMod.ArcaneWars })
            {
                var timer = System.Diagnostics.Stopwatch.StartNew(); Select(window, mod); window.UpdateLayout();
                clickTimes.Add(timer.Elapsed.TotalMilliseconds);
            }
            Check(StateText() == burstState && Settings(window).Mod == GameMod.ArcaneWars, "Rapid offline selection changed the active installation.");
            Check(clickTimes.Max() < 500, "An offline mode click stalled for half a second.");
            Console.WriteLine($"OFFLINE MODE PERFORMANCE {language}: 45 clicks; max={clickTimes.Max():F1}ms; mean={clickTimes.Average():F1}ms; no applied writes");
            var siegeToggle = Control<CheckBox>(window, "SiegeBalanceToggle");
            Check(!siegeToggle.IsEnabled,"A component is selectable while Paw's Patch is disabled");
            var coreToggle=Control<CheckBox>(window,"PawPatchToggle");
            coreToggle.IsChecked=true;coreToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(siegeToggle.IsEnabled, "Offline component toggle is disabled.");
            siegeToggle.IsChecked = true;
            siegeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Check(Settings(window).SiegeBalance, "Component toggle did not change the selected configuration.");
            await ApplyOnly(window);
            await Apply(window);
            Check(File.ReadAllText(Path.Combine(game, "data/cost.txt")) == "0.75", "Previously disabled component could not be applied offline.");
            ChangeLanguage(window, "en");
            Check(GameLanguages.Voice(Settings(window)) == "en", "Legacy combined translation kept an incompatible speech choice.");
            await ApplyOnly(window); await Apply(window);
            Check(File.ReadAllText(Path.Combine(game, "startup/language.txt")) == "en", "Offline English selection was not applied.");
            Select(window, GameMod.Vanilla);
            await ApplyOnly(window); await Apply(window);
            await CheckFiles(window);
            Check(!Settings(window).RussianLocalization && File.ReadAllText(Path.Combine(game, "startup/language.txt")) == "en", "Vanilla changed the common game language.");
            Check(!File.Exists(Path.Combine(game, "data/aw.txt")) && !File.Exists(Path.Combine(game, "data/imm.txt")), "Vanilla retained inactive mod files.");
            ChangeLanguage(window, "ru");
            await ApplyOnly(window); await Apply(window);
            Select(window, GameMod.Immortals);
            await ApplyOnly(window); await Apply(window);
            Check(Settings(window).RussianLocalization && File.ReadAllText(Path.Combine(game, "startup/language.txt")) == "ru", "Immortals did not preserve the chosen language.");
            await CheckFiles(window);

            var offered = JsonSerializer.Deserialize(JsonSerializer.Serialize(initial, LauncherJsonContext.Default.ChannelManifest), LauncherJsonContext.Default.ChannelManifest)!;
            offered.PublishedAt = "2026-09-11T10:00:00Z";
            offered.Packages.RemoveAll(package => package.Id == "pawpatch-core");
            offered.Packages.Add(await Package("pawpatch-core", "2.0.0", 30, new() { ["data/shared.txt"] = "PAW updated" }, true));
            await WriteFeed(offered);
            Connection(window, true);
            Check(await CheckFeed(window), "AW-only update feed was not accepted.");
            Ready(window, GameMod.Immortals);
            await CheckInactiveCompatibility(window, offered);
            Check(!Flag(window, "_patchUpdateAvailable") && !Control<Button>(window, "UpdateButton").IsEnabled, "AW-only update was offered to active Immortals.");
            stateBeforeSelection = StateText();
            Select(window, GameMod.ArcaneWars);
            await ApplyOnly(window);
            Check(StateText() == stateBeforeSelection, "Selecting AW for an offered update modified the active Immortals install.");
            Check(Selected(window).Packages.Single(package => package.Id == "pawpatch-core").Version == "1.0.0", "Selecting stored AW substituted the remote release before Apply.");
            await Apply(window);
            Check(Flag(window, "_patchUpdateAvailable") && Control<Button>(window, "UpdateButton").IsEnabled, "AW update was not offered after AW became active.");
            Check(State().ReleaseId == initialRelease && new ModLibrary(game).Find(GameMod.ArcaneWars, "stable")?.ReleaseId == initialRelease,
                "Apply silently upgraded the stored release while a newer feed was available.");

            RemoveDownloadSources();
            window.Close();
            var reopened = Window(); // Fresh MainWindow, FeedClient and SettingsStore: no in-memory release survives.
            Connection(reopened, false);
            Check(Settings(reopened).Mod == GameMod.ArcaneWars && Settings(reopened).SiegeBalance && Settings(reopened).RussianLocalization,
                "Restart lost the selected mod, component or common language.");
            Check(!await CheckFeed(reopened), "Offline restart unexpectedly reached a feed source.");
            Ready(reopened, GameMod.ArcaneWars);
            Check(Selected(reopened).Packages.Single(package => package.Id == "pawpatch-core").Version == "1.0.0"
                && ChannelFingerprint.Create(Selected(reopened)) == initialRelease, "Offline restart loaded the offered version instead of the installed release.");
            Select(reopened, GameMod.Immortals);
            await ApplyOnly(reopened); await Apply(reopened);
            Check(State().Modules["immortals"].Version == "2.1.0" && File.ReadAllText(Path.Combine(game, "data/shared.txt")) == "IMM",
                "Offline restart could not restore the inactive installed Immortals version.");
            Check(!Flag(reopened, "_patchUpdateAvailable"), "Cached AW-only update leaked into Immortals after restart.");
            Check(new ModLibrary(game).Load().Mods.Select(entry => entry.Mod).Distinct().Count() == 2, "Stored mod entries changed; unmodified Vanilla has no component release.");
            Check(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")) == originalExe, "UI test altered the base executable.");
            Check((await new ModuleInstaller(game).VerifyAsync()).Count == 0, "Final active game files do not match their signed package payloads.");
            Check(blockedNetwork.Attempts == 0, "Fixture attempted an external network request.");
            Console.WriteLine($"MOD LIBRARY UI PASS {count} {language}: install-only, gold Apply, launch lock, offline component/language switching, active-mod updates, fresh-window installed release; signed local ZIPs; no native window/game/network.");
        }

        var dispatcher = Dispatcher.CurrentDispatcher;
        try
        {
            var task = dispatcher.InvokeAsync(Scenario).Task.Unwrap();
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
