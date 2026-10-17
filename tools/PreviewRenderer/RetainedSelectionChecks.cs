using System.Diagnostics;
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
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

// Real signed-catalog migration, controls and install transactions. All game,
// account, package and profile data are disposable; k2.exe is a text fixture.
internal static class RetainedSelectionChecks
{
    internal static void Run(string language, string channelName, string output)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null)
            throw new InvalidOperationException("Disposable smoke profile required.");
        var root = Path.Combine(ActivityStore.Root, "retained-selection");
        var game = Path.Combine(root, "game");
        var sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(game); Directory.CreateDirectory(sources); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(game, "k2.exe"), "Non-executable migration fixture.");
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var network = new RejectNetwork();
        using var http = new HttpClient(network);
        var feedPath = Path.Combine(sources, "catalog.json");
        var configuration = new LauncherConfiguration { FeedUrls = [feedPath], BetaFeedUrls = [feedPath],
            CacheRoot = Path.Combine(root, "cache"), PublicKeyPem = signing.ExportSubjectPublicKeyInfoPem() };
        var client = new FeedClient(configuration, http);
        var installer = new ModuleInstaller(game);
        var library = new ModLibrary(game);
        MainWindow? window = null;
        var n = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception("Retained selection: " + reason); n++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        T C<T>(string name) => (T)window!.FindName(name);
        UserSettings Prefs() => (UserSettings)Field("_settings")!;
        string StateText() => File.ReadAllText(Path.Combine(game, ".pawpatch", "state.json"));
        void Select(string mod)
        {
            C<RadioButton>(mod == GameMod.Immortals ? "ImmortalsModRadio" : "VanillaModRadio").IsChecked = true;
            Check(Prefs().Mod == mod && Prefs().Channel == channelName, "Mod click lost its remembered channel.");
        }
        void Languages(bool russian, string voice)
        {
            Prefs().RussianLocalization = russian; Prefs().GameVoiceLanguage = voice;
            Call("RefreshStatus");
        }
        void UpdateRequired(bool available = true)
        {
            Check(Field("_installationFailure") is null, "Unsupported old selection is reported as damaged files.");
            Check((bool)Field("_settingsPending")! && !C<Button>("LaunchButton").IsEnabled, "Unapplied selection can launch.");
            Check(!C<Button>("ApplySettingsButton").IsEnabled && !C<Button>("ApplySettingsButton").IsHitTestVisible,
                "Apply bypasses the required complete update.");
            Check(C<Button>("UpdateButton").IsEnabled == available, "Update and apply availability is wrong.");
            Check(Equals(C<Button>("UpdateButton").Content, available
                ? language == "ru" ? "Обновить и применить" : "Update and apply"
                : language == "ru" ? "Нужно обновление" : "Update required"), "Update action is not explicit.");
            Check(C<TextBlock>("ReadyStatusText").Text == (language == "ru" ? "Нужно обновить файлы мода" : "Mod files need an update"),
                "Header still requests file verification.");
            Check(C<Border>("OperationStatusPanel").Visibility == Visibility.Visible, "The required update has no visible explanation.");
        }
        async Task<PackageRelease> Package(string id, int priority, string path, string body, string version = "1.0.0", bool required = false)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            var archive = Path.Combine(sources, id + "-" + version + ".zip");
            var manifest = new ModuleArchiveManifest { Id = id, Version = version, Files = [new() {
                Path = path, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }] };
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry("payload/" + path).Open()) stream.Write(bytes);
                using var writer = new StreamWriter(zip.CreateEntry("module.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.ModuleArchiveManifest));
            }
            return new() { Id = id, Version = version, Required = required, Name = new() { Ru = id, En = id }, Priority = priority,
                Size = new FileInfo(archive).Length, Sha256 = await CryptoAndIO.Sha256Async(archive), Urls = [archive], ExecutableIndependent = true };
        }
        async Task WriteFeed(ChannelManifest feed)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(feed, LauncherJsonContext.Default.ChannelManifest);
            var envelope = new SignedFeedEnvelope { KeyId = "retained-fixture", Payload = Convert.ToBase64String(payload),
                Signature = Convert.ToBase64String(signing.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            await File.WriteAllTextAsync(feedPath, JsonSerializer.Serialize(envelope, LauncherJsonContext.Default.SignedFeedEnvelope));
        }
        async Task ApplyFixture(ChannelManifest feed, UserSettings prefs)
        {
            var active = EffectiveSettings.ForFeed(prefs, feed);
            var selected = GamePackageSelector.Select(feed, active, active.RussianLocalization, active.CustomPlayerColors);
            var prepared = new Dictionary<string, InstalledModule>();
            foreach (var p in selected) prepared[p.Id] = await installer.PrepareAsync(p, p.Urls[0]);
            await installer.ReconcileAsync(prepared, settings: active, releaseId: ChannelFingerprint.Create(feed));
        }
        async Task Scenario()
        {
            var exeHash = await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe"));
            var legacy = new ChannelManifest { Channel = channelName, PublishedAt = "2026-09-09T00:00:00Z", Game = new() { K2ExeSha256 = [exeHash] }, Packages = [
                await Package("startup-base", 20, "startup/base.txt", "startup", required: true),
                await Package("immortals", 10, "data/mod.txt", "old Immortals"),
                await Package("game-localization-en", 100, "startup/language.txt", "en"),
                await Package("vanilla-localization-ru", 110, "startup/language.txt", "ru"),
                await Package("immortals-localization-ru", 110, "startup/language.txt", "ru Immortals"),
                await Package("pure-fixes-data", 900, "data/pure-fix.txt", "old pure fix"),
                await Package("pure-fixes-runtime", 910, "k2_paws_pure_fixes_1372.exe", "Non-executable fixture") ] };
            await WriteFeed(legacy); await client.GetChannelAsync(channelName);
            foreach (var p in legacy.Packages) await installer.PrepareAsync(p, p.Urls[0]);
            foreach (var mod in new[] { GameMod.Immortals, GameMod.Vanilla }) await library.RememberAsync(legacy, mod);
            var current = new ChannelManifest { Channel = channelName, PublishedAt = "2026-09-14T00:00:00Z", Game = legacy.Game,
                Packages = legacy.Packages.Where(p => p.Id is not ("immortals" or "pure-fixes-data")).ToList() };
            current.Packages.AddRange([
                await Package("immortals", 10, "data/mod.txt", "new Immortals", "2.0.0"),
                await Package("pure-fixes-data", 900, "data/pure-fix.txt", "new pure fix", "2.0.0"),
                await Package("game-voice-ru", 120, "voices/ru.txt", "Russian speech") ]);
            await WriteFeed(current); await client.GetChannelAsync(channelName);
            var prefs = new UserSettings { Language = language, GamePath = game, Mod = GameMod.Vanilla, Channel = channelName,
                RussianLocalization = true, GameVoiceLanguage = "en", ModNoticeSeen = true, VanillaPawPatchEnabled = true };
            foreach (var mod in new[] { GameMod.Immortals, GameMod.Vanilla }) prefs.ModChannels[mod] = new() { Channel = channelName };
            foreach (var p in ModLibrary.Packages(current, GameMod.Vanilla)) await installer.PrepareAsync(p, p.Urls[0]);
            await ApplyFixture(current, prefs); await library.RememberAsync(current, GameMod.Vanilla);
            new SettingsStore().Save(prefs);
            window = new MainWindow(configuration, client) { Width = 1440, Height = 900, ShowActivated = false, ShowInTaskbar = false };
            Set("_game", new GameInstallation(game, Path.Combine(game, "k2.exe"), "fixture", channelName));
            Set("_gameRunningProbe", new Func<bool>(() => false));
            Call("SetActivePage", "modules");
            Check(await (Task<bool>)Call("CheckFeedAsync", false)!, "Current signed catalog was rejected.");
            var before = StateText(); var libraryBefore = File.ReadAllText(Path.Combine(game, ".pawpatch", "mod-library.json"));
            foreach (var mod in new[] { GameMod.Immortals })
            {
                Select(mod);
                Check(Prefs().RussianLocalization && GameLanguages.Voice(Prefs()) == "en", "Selecting a retained mod changed global language choices.");
                UpdateRequired();
                foreach (var russian in new[] { false, true })
                foreach (var voice in new[] { "en", "ru" })
                {
                    Languages(russian, voice);
                    if (voice != (russian ? "ru" : "en")) UpdateRequired();
                    else Check(Field("_installationFailure") is null && C<Button>("ApplySettingsButton").IsEnabled && !C<Button>("UpdateButton").IsEnabled,
                        "Supported retained settings cannot be applied offline without updating.");
                }
                Languages(true, "en");
            }
            Select(GameMod.Immortals);
            Languages(false, "en");
            Check(C<ComboBox>("GameVoiceCombo").IsEnabled, "Known modern catalog does not allow choosing separate speech.");
            C<ComboBox>("GameVoiceCombo").SelectedIndex = 1; UpdateRequired();
            C<ComboBox>("GameVoiceCombo").SelectedIndex = 0;
            Check(C<Button>("ApplySettingsButton").IsEnabled && !C<Button>("UpdateButton").IsEnabled, "Returning speech to the original choice left an update requirement.");
            Languages(true, "en");
            Prefs().PinnedRelease = ChannelFingerprint.Create(legacy); Call("RefreshStatus"); UpdateRequired(false);
            await (Task)Call("ApplySettingsAsync")!;
            Check(StateText() == before, "Direct Apply bypassed the pinned release guard.");
            Prefs().PinnedRelease = null;
            Set("_offeredModChannel", null); Call("RefreshStatus"); UpdateRequired(false);
            Set("_offeredModChannel", current); Call("RefreshStatus"); UpdateRequired();
            var worst = 0d;
            for (var i = 0; i < 45; i++)
            {
                var watch = Stopwatch.StartNew(); Select(i % 2 == 0 ? GameMod.Vanilla : GameMod.Immortals);
                worst = Math.Max(worst, watch.Elapsed.TotalMilliseconds);
                if (Prefs().Mod == GameMod.Vanilla) Check(!C<Button>("UpdateButton").IsEnabled && !(bool)Field("_settingsPending")!, "Immortals update leaked into unchanged Vanilla.");
                else UpdateRequired();
            }
            Check(worst < 500, $"Mod clicks block for {worst:F0} ms.");
            Check(StateText() == before && File.ReadAllText(Path.Combine(game, ".pawpatch", "mod-library.json")) == libraryBefore,
                "Previewing selections wrote game files or upgraded retained releases.");
            Select(GameMod.Immortals); Call("RefreshStatus"); UpdateRequired();
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1440, 900)); content.Arrange(new Rect(0, 0, 1440, 900)); content.UpdateLayout();
            await Task.Delay(350); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, $"retained-{channelName}-{language}.png"))) encoder.Save(stream);
            using (var reader = new FileStream(Path.Combine(game, ".pawpatch", "state.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                C<Button>("UpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(400);
                Check((bool)Field("_busy")! && StateText() == before, "Temporary state-file lock escaped the explicit update transaction.");
            }
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while ((bool)Field("_busy")! && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
            var state = installer.LoadState();
            Check(!(bool)Field("_busy")! && Field("_installationFailure") is null, "Update and apply failed.");
            Check(state.AppliedSettings?.Mod == GameMod.Immortals && state.AppliedSettings.RussianLocalization && state.AppliedSettings.GameVoiceLanguage == "en",
                "Update did not preserve and apply the selected languages.");
            Check(state.Modules["immortals"].Version == "2.0.0" && !state.Modules.ContainsKey("game-voice-ru"), "Wrong mod or unselected speech applied.");
            Check(library.Find(GameMod.Immortals, channelName)?.ReleaseId == ChannelFingerprint.Create(current), "Updated mod was not retained.");
            Check(!C<Button>("UpdateButton").IsEnabled && !(bool)Field("_settingsPending")!, "Completed update remains pending.");
            // The same migration applies to an older retained Vanilla patch,
            // independently of the mod currently active in the game.
            await library.RememberAsync(legacy, GameMod.Vanilla);
            foreach (var mod in new[] { GameMod.Vanilla })
            {
                Select(mod); UpdateRequired();
                C<Button>("UpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                deadline = DateTimeOffset.UtcNow.AddSeconds(15);
                while ((bool)Field("_busy")! && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
                state = installer.LoadState();
                Check(!(bool)Field("_busy")! && Field("_installationFailure") is null && !(bool)Field("_settingsPending")!, "Vanilla update failed.");
                Check(state.AppliedSettings?.Mod == mod && state.AppliedSettings.RussianLocalization && state.AppliedSettings.GameVoiceLanguage == "en",
                    "Vanilla update changed language choices.");
                Check(library.Find(mod, channelName)?.ReleaseId == ChannelFingerprint.Create(current), "Vanilla update was not retained.");
            }
            Check(!installer.IsPrepared(current.Packages.Single(p => p.Id == "game-voice-ru")), "Unselected Russian speech was downloaded.");
            Select(GameMod.Immortals);
            var olderOffer = JsonSerializer.Deserialize(JsonSerializer.Serialize(current, LauncherJsonContext.Default.ChannelManifest), LauncherJsonContext.Default.ChannelManifest)!;
            olderOffer.Packages.RemoveAll(p => p.Id == "game-voice-ru");
            Set("_offeredModChannel", olderOffer); Languages(false, "ru");
            Check(Field("_installationFailure") is null && C<ComboBox>("GameVoiceCombo").IsEnabled && C<Button>("ApplySettingsButton").IsEnabled,
                "An older offered language catalog broke the modern retained release.");
            Set("_offeredModChannel", current); Languages(true, "en");
            // All required packages are now retained; actual switching works with
            // every download source removed, including the two selected languages.
            foreach (var path in Directory.GetFiles(sources)) File.Delete(path);
            Select(GameMod.Immortals); Check(C<Button>("ApplySettingsButton").IsEnabled, "Updated Immortals cannot apply offline.");
            await (Task)Call("ApplySettingsAsync")!;
            Check(installer.LoadState().AppliedSettings?.Mod == GameMod.Immortals && !(bool)Field("_settingsPending")! && Field("_installationFailure") is null,
                "Offline switch failed after successful update.");
            Select(GameMod.Vanilla); Check(C<Button>("ApplySettingsButton").IsEnabled, "Stored Vanilla cannot apply offline.");
            await (Task)Call("ApplySettingsAsync")!;
            Check(installer.LoadState().AppliedSettings?.Mod == GameMod.Vanilla && !(bool)Field("_settingsPending")! && Field("_installationFailure") is null,
                "Offline switch back to Vanilla failed.");
            Check(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")) == exeHash, "Original game executable changed.");
            Check(network.Attempts == 0, "Scenario attempted external network access.");
            Console.WriteLine($"RETAINED SELECTION PASS {n} {channelName}/{language}: split languages, pins, no offer, 45 clicks (max {worst:F1} ms), explicit update, offline reuse.");
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
            if (!task.IsCompleted) throw new TimeoutException("Retained selection check timed out.");
            task.GetAwaiter().GetResult();
        }
        finally { if (window is not null) { Set("_busy", false); window.Close(); } }
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Attempts++; throw new InvalidOperationException("External network forbidden in retained selection fixture."); }
    }
}
