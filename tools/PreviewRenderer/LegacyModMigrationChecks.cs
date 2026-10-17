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

// Reproduces the 0.6.4 -> 0.7.0 empty Vanilla library entry using signed local
// catalogs and actual MainWindow commands. Never reads the real game or account.
internal static class LegacyModMigrationChecks
{
    internal static void Run(string language, string output)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null)
            throw new InvalidOperationException("Disposable smoke profile required.");
        var root = Path.Combine(ActivityStore.Root, "legacy-migration");
        var game = Path.Combine(root, "game");
        var sources = Path.Combine(root, "sources");
        Directory.CreateDirectory(game); Directory.CreateDirectory(sources); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(game, "k2.exe"), "Non-executable migration fixture.");
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var network = new RejectNetwork();
        using var http = new HttpClient(network);
        var configuration = new LauncherConfiguration { FeedUrls = [Path.Combine(sources, "stable.json")], BetaFeedUrls = [],
            CacheRoot = Path.Combine(root, "cache"), PublicKeyPem = signing.ExportSubjectPublicKeyInfoPem() };
        var client = new FeedClient(configuration, http);
        var installer = new ModuleInstaller(game);
        var library = new ModLibrary(game);
        MainWindow? window = null;
        var n = 0;
        void Check(bool ok, string reason) { if (!ok) throw new Exception("Legacy migration: " + reason); n++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, args);
        T Control<T>(string name) => (T)window!.FindName(name);
        async Task<PackageRelease> Package(string id, int priority, string path, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var archive = Path.Combine(sources, id + ".zip");
            var manifest = new ModuleArchiveManifest { Id = id, Version = "1.0.0", Files = [new() {
                Path = path, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) }] };
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                using (var stream = zip.CreateEntry("payload/" + path).Open()) stream.Write(bytes);
                using var writer = new StreamWriter(zip.CreateEntry("module.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.ModuleArchiveManifest));
            }
            return new() { Id = id, Version = "1.0.0", Name = new() { Ru = id, En = id }, Priority = priority,
                Size = new FileInfo(archive).Length, Sha256 = await CryptoAndIO.Sha256Async(archive), Urls = [archive], ExecutableIndependent = true };
        }
        async Task WriteFeed(ChannelManifest feed)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(feed, LauncherJsonContext.Default.ChannelManifest);
            var envelope = new SignedFeedEnvelope { KeyId = "migration-fixture", Payload = Convert.ToBase64String(payload),
                Signature = Convert.ToBase64String(signing.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            await File.WriteAllTextAsync(configuration.FeedUrls[0], JsonSerializer.Serialize(envelope, LauncherJsonContext.Default.SignedFeedEnvelope));
        }
        void InstallAvailable()
        {
            Check(Control<Border>("CoreModuleCard").Visibility == Visibility.Visible, "Paw's Patch disappeared from Vanilla.");
            Check(Control<Border>("VanillaEmptyCard").Visibility == Visibility.Collapsed, "Old empty-mode placeholder hides the patch.");
            Check(Control<CheckBox>("PawPatchToggle").IsEnabled, "Vanilla patch cannot be selected.");
            Check(Control<Button>("UpdateButton").IsEnabled && !(bool)Field("_patchInstalled")!, "Missing Vanilla components have no Install action.");
            Check(!Control<Button>("ApplySettingsButton").IsEnabled && !Control<Button>("LaunchButton").IsEnabled, "Missing components can be applied or launched.");
            Check(Field("_installationFailure") is null, "Legacy catalog still causes a missing-language error.");
        }
        async Task Scenario()
        {
            var exeHash = await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe"));
            var legacy = new ChannelManifest { PublishedAt = "2026-09-09T00:00:00Z", Game = new() { K2ExeSha256 = [exeHash] }, Packages = [
                await Package("arcane-wars", 10, "data/mod.txt", "Arcane Wars"),
                await Package("startup-base", 20, "startup/base.txt", "startup"),
                await Package("pawpatch-core", 30, "data/patch.txt", "old AW patch") ] };
            await WriteFeed(legacy); await client.GetChannelAsync();
            var prepared = new Dictionary<string, InstalledModule>();
            foreach (var p in legacy.Packages) prepared[p.Id] = await installer.PrepareAsync(p, p.Urls[0]);
            await installer.ReconcileAsync(prepared, settings: new UserSettings { Mod = GameMod.ArcaneWars, Channel = "stable" }, releaseId: ChannelFingerprint.Create(legacy));
            await library.RememberAsync(legacy, GameMod.ArcaneWars);
            var document = library.Load();
            // Existing affected profiles must recover, not only new migrations.
            document.Mods.Add(new() { Mod = GameMod.Vanilla, Channel = "stable", ReleaseId = ChannelFingerprint.Create(legacy),
                ContentId = ModLibrary.ContentId(legacy, GameMod.Vanilla), Packages = [] });
            await File.WriteAllTextAsync(Path.Combine(game, ".pawpatch", "mod-library.json"), JsonSerializer.Serialize(document, LauncherJsonContext.Default.ModLibraryDocument));
            var current = new ChannelManifest { PublishedAt = "2026-09-14T00:00:00Z", Game = legacy.Game, Packages = [..legacy.Packages,
                await Package("game-localization-en", 90, "startup/language.txt", "en"),
                await Package("vanilla-localization-ru", 100, "startup/language.txt", "ru"),
                await Package("game-voice-ru", 110, "voices/ru.txt", "Russian speech"),
                await Package("immortals", 10, "data/mod.txt", "Immortals"),
                await Package("immortals-localization-ru", 100, "startup/language.txt", "ru Immortals"),
                await Package("pure-fixes-data", 900, "data/pure-fix.txt", "badge colors"),
                await Package("pure-fixes-runtime", 910, "k2_paws_pure_fixes_1372.exe", "Non-executable pure-fix fixture") ] };
            await WriteFeed(current);
            new SettingsStore().Save(new UserSettings { Language = language, GamePath = game, Mod = GameMod.Vanilla,
                Channel = "stable", RussianLocalization = true, GameVoiceLanguage = "ru", ModNoticeSeen = true });
            window = new MainWindow(configuration, client) { Width = 1440, Height = 900, ShowActivated = false, ShowInTaskbar = false };
            Set("_game", new GameInstallation(game, Path.Combine(game, "k2.exe"), "fixture", "stable"));
            Set("_gameRunningProbe", new Func<bool>(() => false));
            Call("SetActivePage", "modules");
            Check(Equals(Control<RadioButton>("VanillaModRadio").Content, "Vanilla"), "Vanilla selector name is translated.");
            var stateBefore = File.ReadAllText(Path.Combine(game, ".pawpatch", "state.json"));
            Check(await (Task<bool>)Call("CheckFeedAsync", false)!, "Current signed catalog was rejected.");
            InstallAvailable();
            Check(library.Find(GameMod.Vanilla, "stable") is null, "Empty legacy entry is still reported as an installed mod.");
            Check(File.ReadAllText(Path.Combine(game, ".pawpatch", "state.json")) == stateBefore, "Migration silently applied game changes.");
            foreach (var text in new[] { false, true })
            foreach (var voice in new[] { "en", "ru" })
            {
                var settings = (UserSettings)Field("_settings")!;
                settings.RussianLocalization = text; settings.GameVoiceLanguage = voice;
                Call("RefreshStatus"); InstallAvailable();
            }
            var prefs = (UserSettings)Field("_settings")!;
            prefs.RussianLocalization = true; prefs.GameVoiceLanguage = "ru";
            Call("ApplyLanguage");
            var content = (FrameworkElement)window.Content;
            content.Measure(new Size(1440, 900)); content.Arrange(new Rect(0, 0, 1440, 900)); content.UpdateLayout();
            await Task.Delay(350);
            content.UpdateLayout();
            var bitmap = new RenderTargetBitmap(1440, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "migration-" + language + ".png"))) encoder.Save(stream);
            GameMod.SetPawPatch(prefs, true);
            Control<Button>("UpdateButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);
            while ((bool)Field("_busy")! && DateTimeOffset.UtcNow < deadline) await Task.Delay(20);
            Check(!(bool)Field("_busy")! && Field("_installationFailure") is null, "Vanilla installation failed.");
            Check(installer.LoadState().AppliedSettings?.Mod == GameMod.Vanilla && installer.LoadState().Modules.ContainsKey("pure-fixes-data"), "Install did not apply the selected Vanilla patch.");
            Check(library.Find(GameMod.Vanilla, "stable")?.Packages.Count == 2, "Installed Vanilla patch was not retained.");
            Check(library.Find(GameMod.ArcaneWars, "stable")?.ReleaseId == ChannelFingerprint.Create(legacy), "Migration upgraded or discarded installed Arcane Wars.");
            Check(!File.Exists(Path.Combine(game, "data/mod.txt")), "Vanilla kept active Arcane Wars files.");
            Check(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")) == exeHash, "Original EXE changed.");
            // Once installed, an offline refresh keeps the real retained release.
            File.Delete(configuration.FeedUrls[0]);
            Check(!await (Task<bool>)Call("CheckFeedAsync", false)!, "Offline check unexpectedly succeeded.");
            Check((bool)Field("_patchInstalled")! && !(bool)Field("_settingsPending")!, "Offline installed Vanilla was lost.");
            Check(network.Attempts == 0, "Migration contacted an external service.");
            Console.WriteLine($"LEGACY MOD MIGRATION PASS {n} {language}: affected empty entry, current catalog, four language pairs, actual install, retained AW, offline reuse.");
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
            if (!task.IsCompleted) throw new TimeoutException("Migration check timed out.");
            task.GetAwaiter().GetResult();
        }
        finally { if (window is not null) { Set("_busy", false); window.Close(); } }
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Attempts++; throw new InvalidOperationException("External network forbidden in migration fixture."); }
    }
}
