using System.Net;
using System.Net.Http;
using System.Text.Json;
using PawsPatchLauncher;

internal static class EuropeanLanguageTests
{
    internal static async Task StageGameAsync(string configPath, string root, string text, string mod = GameMod.Vanilla)
    {
        root = Path.GetFullPath(root);
        if (!File.Exists(Path.Combine(root, "result.json"))) throw new IOException("A completed isolated language test fixture is required.");
        if (mod is not GameMod.Vanilla and not GameMod.Immortals and not GameMod.ArcaneWars)
            throw new ArgumentException("Unknown fixture mod", nameof(mod));
        var config = JsonSerializer.Deserialize(File.ReadAllText(configPath), LauncherJsonContext.Default.LauncherConfiguration)!;
        config.CacheRoot = Path.Combine(root, "cache");
        var client = new FeedClient(config); var feed = (await client.GetChannelAsync())!;
        var settings = new UserSettings { Mod = mod, PawPatchEnabled = false, GameVoiceLanguage = "en", GamePath = Path.Combine(root, "game") };
        GameMod.SetPawPatch(settings, false);
        GameLanguages.SetText(settings, text);
        var installer = new ModuleInstaller(settings.GamePath);
        var modules = new Dictionary<string, InstalledModule>();
        foreach (var package in GamePackageSelector.Select(feed, settings, settings.RussianLocalization, false))
            modules[package.Id] = await installer.PrepareAsync(package, await client.DownloadVerifiedAsync(package, null));
        await installer.ReconcileAsync(modules, settings: settings, releaseId: ChannelFingerprint.Create(feed));
        Console.WriteLine("Staged isolated game text: " + text + "; mod: " + mod);
    }
    private sealed class NoNetwork : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Requests++; throw new HttpRequestException("Offline test: network is unavailable."); }
    }

    internal static async Task RunAsync(string configPath, string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose an unused isolated test directory.");
        Directory.CreateDirectory(root);
        var config = JsonSerializer.Deserialize(File.ReadAllText(configPath), LauncherJsonContext.Default.LauncherConfiguration)!;
        config.CacheRoot = Path.Combine(root, "cache");
        var client = new FeedClient(config);
        int checks = 0, combinations = 0;
        void Check(bool value, string why) { if (!value) throw new Exception("European languages: " + why); checks++; }
        UserSettings Selection(string mod, string channel, string text, string voice, bool patch, bool data)
        {
            var s = new UserSettings { Mod = mod, Channel = channel, GameVoiceLanguage = voice, DataOnly = data };
            GameLanguages.SetText(s, text); GameMod.SetPawPatch(s, patch);
            return EffectiveSettings.ForChannel(s);
        }
        ChannelManifest? stable = null;
        foreach (var channel in new[] { "stable", "beta" })
        {
            var feed = (await client.GetChannelAsync(channel))!;
            if (channel == "stable") stable = feed;
            foreach (var mod in new[] { GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars })
            foreach (var text in GameLanguages.Choices)
            foreach (var voice in GameLanguages.VoiceChoices)
            foreach (var patch in new[] { false, true })
            foreach (var data in new[] { false, true })
            {
                combinations++;
                var s = Selection(mod, channel, text, voice, patch, data);
                var selected = GamePackageSelector.Select(feed, s, s.RussianLocalization, s.CustomPlayerColors);
                Check(selected.Select(p => p.Id).Distinct().Count() == selected.Count, "duplicate package");
                Check(selected.All(p => p.DependsOn.All(d => selected.Any(q => q.Id == d))), "dependency missing");
                Check(selected.Where(p => p.Id.StartsWith("game-voice-")).Select(p => p.Id)
                    .SequenceEqual(voice == "en" ? [] : new[] { "game-voice-" + voice }), "voice differs from selection");
                foreach (var code in new[] { "de", "fr", "cs", "uk" })
                {
                    Check(selected.Any(p => p.Id == "game-localization-" + code) == (text == code), "unselected text included");
                    foreach (var prefix in new[] { "immortals-localization-", "localization-", "aw-localization-", "pawpatch-data-" })
                    {
                        var id = prefix + code;
                        if (!feed.Packages.Any(p => p.Id == id)) continue;
                        var expected = text == code && (prefix == "immortals-localization-" ? mod == GameMod.Immortals
                            : mod == GameMod.ArcaneWars && (prefix == "localization-" ? patch && !data
                                : prefix == "aw-localization-" ? !patch || data : patch && data));
                        Check(selected.Any(p => p.Id == id) == expected, "wrong mod translation: " + id);
                        Check(!(expected && prefix == "pawpatch-data-") || selected.All(p => p.Id != "pawpatch-data"), "duplicate file-only patch");
                    }
                }
                Check(ModLibrary.Packages(feed, mod).All(p => !GameLanguages.IsLanguage(p)), "install fetches every language");
                Check(!s.DataOnly || selected.All(p => p.ExecutableIndependent), "text requires executable");
                var codeString = ConfigurationCode.Create(s);
                var parsed = ConfigurationCode.Parse(codeString);
                Check(GameLanguages.Text(parsed) == text && GameLanguages.Voice(parsed) == voice, "configuration round trip");
                var saved = JsonSerializer.Deserialize(JsonSerializer.Serialize(s, LauncherJsonContext.Default.UserSettings), LauncherJsonContext.Default.UserSettings)!;
                Check(GameLanguages.Text(saved) == text && GameLanguages.Voice(saved) == voice, "saved profile loses languages");
                var english = Selection(mod, channel, "en", "en", patch, data);
                ModChannelSelection.SelectMod(saved, mod == GameMod.Vanilla ? GameMod.Immortals : GameMod.Vanilla);
                Check(GameLanguages.Text(saved) == text && GameLanguages.Voice(saved) == voice, "mod switch loses language");
                var state = new InstallState { AppliedSettings = s };
                Check(!UpdateDetector.HasSettingsChanges(state, [], saved = EffectiveSettings.ForChannel(s)), "unchanged settings marked pending");
                GameLanguages.SetText(saved, text == "de" ? "fr" : "de");
                Check(UpdateDetector.HasSettingsChanges(state, [], saved), "text switch not marked pending");
                GameLanguages.SetText(saved, text);
                Check(!UpdateDetector.HasSettingsChanges(state, [], saved), "text reversal left pending changes");
                foreach (var code in new[] { "de", "fr", "cs", "uk" })
                {
                    var changed = JsonSerializer.Deserialize(JsonSerializer.Serialize(feed, LauncherJsonContext.Default.ChannelManifest), LauncherJsonContext.Default.ChannelManifest)!;
                    if (GameLanguages.VoiceChoices.Contains(code))
                    {
                        changed.Packages.Single(p => p.Id == "game-voice-" + code).Version += ".new";
                        Check(!ModLibrary.HasUpdate(feed, changed, mod), "voice update marks whole mod outdated");
                        Check(GameLanguages.HasUpdate(feed, changed, s) == (voice == code), "unselected voice update shown");
                        changed.Packages.Single(p => p.Id == "game-voice-" + code).Version = feed.Packages.Single(p => p.Id == "game-voice-" + code).Version;
                    }
                    changed.Packages.Single(p => p.Id == "game-localization-" + code).Version += ".new";
                    Check(GameLanguages.HasUpdate(feed, changed, s) == (text == code), "unselected text update shown");
                    foreach (var prefix in new[] { "immortals-localization-", "localization-", "aw-localization-", "pawpatch-data-" })
                    {
                        var update = JsonSerializer.Deserialize(JsonSerializer.Serialize(feed, LauncherJsonContext.Default.ChannelManifest), LauncherJsonContext.Default.ChannelManifest)!;
                        var target = update.Packages.SingleOrDefault(p => p.Id == prefix + code);
                        if (target is null) continue;
                        target.Version += ".new";
                        Check(!ModLibrary.HasUpdate(feed, update, mod), "mod translation fetches all languages");
                        Check(GameLanguages.HasUpdate(feed, update, s) == selected.Any(p => p.Id == target.Id), "unselected mod translation update shown");
                    }
                }
            }
        }
        foreach (var ru in new[] { false, true })
            Check(GameLanguages.Text(new() { RussianLocalization = ru }) == (ru ? "ru" : "en")
                && GameLanguages.Voice(new() { RussianLocalization = ru }) == (ru ? "ru" : "en"), "legacy migration");
        foreach (var language in UiLanguages.Choices)
            Check(UiLanguages.GameLanguageName("de", language.Code) != UiLanguages.GameLanguageName("fr", language.Code), "language labels equal");
        foreach (var text in new[] { "cs", "uk" })
        {
            var migrated = new UserSettings(); GameLanguages.SetText(migrated, text);
            Check(GameLanguages.Voice(migrated) == "en", "text-only language invented a voice");
            var restored = ConfigurationCode.Parse(ConfigurationCode.Create(migrated));
            Check(GameLanguages.Text(restored) == text && GameLanguages.Voice(restored) == "en", "text-only language round trip");
            Check(!stable!.Packages.Any(p => p.Id == "game-voice-" + text), "invented voice package");
        }

        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var installer = new ModuleInstaller(game);
        var library = new ModLibrary(game);
        // Real package preparation/reconciliation, with only language resources selected.
        async Task Apply(string text, string voice, FeedClient source)
        {
            var settings = Selection(GameMod.Vanilla, "stable", text, voice, false, false);
            var selected = GamePackageSelector.Select(stable!, settings, settings.RussianLocalization, false).Where(GameLanguages.IsLanguage).ToList();
            var modules = new Dictionary<string, InstalledModule>();
            foreach (var package in selected)
                modules[package.Id] = await installer.PrepareAsync(package, await source.DownloadVerifiedAsync(package, null));
            await installer.ReconcileAsync(modules, settings: settings, releaseId: ChannelFingerprint.Create(stable!));
            await library.RememberLanguagesAsync(selected);
            Check((await installer.VerifyAsync()).Count == 0, "active language files damaged");
            Check(!UpdateDetector.HasSettingsChanges(installer.LoadState(), selected, settings), "apply left settings pending");
            if (text is "de" or "fr" or "cs" or "uk")
                Check(File.ReadAllText(Path.Combine(game, "startup", "autoexec_ru.txt")).Contains("Local_base_" + text + "/"), "wrong locale mounted");
            var oldDepot = Path.Combine(game, "Local_base_" + (text == "de" ? "fr" : "de"));
            Check(!Directory.Exists(oldDepot) || !Directory.EnumerateFiles(oldDepot, "*", SearchOption.AllDirectories).Any(), "previous text files remained");
        }
        await Apply("de", "en", client);
        Check(!client.IsPackageCached(stable!.Packages.Single(p => p.Id == "game-voice-de")), "text fetched speech");
        Check(!client.IsPackageCached(stable.Packages.Single(p => p.Id == "game-localization-fr")), "German fetched French");
        await Apply("fr", "de", client);
        Check(!client.IsPackageCached(stable.Packages.Single(p => p.Id == "game-voice-fr")), "French text fetched French speech");
        await Apply("de", "fr", client);
        await Apply("ru", "ru", client);
        await Apply("cs", "de", client);
        await Apply("uk", "fr", client);
        var modReconciliations = 0;
        async Task ApplyMod(string mod, string text, bool patch, bool data, FeedClient source)
        {
            var settings = Selection(mod, "stable", text, "en", patch, data);
            var selected = GamePackageSelector.Select(stable!, settings, settings.RussianLocalization, false).Where(GameLanguages.IsLanguage).ToList();
            var modules = new Dictionary<string, InstalledModule>();
            foreach (var package in selected)
                modules[package.Id] = await installer.PrepareAsync(package, await source.DownloadVerifiedAsync(package, null));
            await installer.ReconcileAsync(modules, settings: settings, releaseId: ChannelFingerprint.Create(stable!));
            await library.RememberLanguagesAsync(selected);
            Check((await installer.VerifyAsync()).Count == 0, "mod language files damaged");
            var depot = "Local_" + (mod == GameMod.Immortals ? "immortals" : "aw") + "_" + text;
            var mounts = File.ReadAllText(Path.Combine(game, "startup", "autoexec_ru.txt"));
            Check(mounts.Contains("Local_base_" + text + "/") && mounts.Contains(depot + "/"), "mod locale not mounted");
            var name = mod == GameMod.Immortals ? "strings_immortals_translation.tgi" : "strings_data_K2.tgi";
            var dictionary = File.ReadAllText(Path.Combine(game, depot, "Localization", name));
            var expected = mod == GameMod.Immortals
                ? text switch { "de" => "Tierbändiger", "fr" => "Dresseur de bêtes", "cs" => "Krotitel", _ => "Приборкувач" }
                : text switch { "de" => "Bauplatz", "fr" => "Emplacement de construction", "cs" => "Staveniště", _ => "Місце під фундамент" };
            Check(dictionary.Contains(expected), "semantic correction missing");
            var oldDepots = (from family in new[] { "aw", "immortals" } from code in new[] { "de", "fr", "cs", "uk" } select "Local_"+family+"_"+code).Where(d => d != depot);
            Check(oldDepots.All(d => !Directory.Exists(Path.Combine(game, d)) || !Directory.EnumerateFiles(Path.Combine(game, d), "*", SearchOption.AllDirectories).Any()), "previous mod locale remained");
            modReconciliations++;
        }
        var modSelections = (from mod in new[] { GameMod.Immortals, GameMod.ArcaneWars }
                             from text in new[] { "de", "fr", "cs", "uk" }
                             from patch in new[] { false, true }
                             from data in new[] { false, true }
                             select (mod, text, patch, data)).ToArray();
        if (stable.Packages.Any(p => p.Id == "immortals-localization-de"))
            foreach (var (mod, text, patch, data) in modSelections) await ApplyMod(mod, text, patch, data, client);
        var before = Directory.GetFiles(config.CacheRoot, "*.zip", SearchOption.AllDirectories).ToDictionary(p => p, File.GetLastWriteTimeUtc);
        using var network = new NoNetwork(); using var http = new HttpClient(network);
        var offline = new FeedClient(config, http);
        foreach (var p in stable.Packages) p.Urls = ["https://offline.invalid/" + p.Id];
        if (modReconciliations > 0)
            foreach (var (mod, text, patch, data) in modSelections.Reverse()) await ApplyMod(mod, text, patch, data, offline);
        foreach (var pair in new[] { ("cs", "ru"), ("uk", "de"), ("fr", "ru"), ("ru", "de"), ("de", "fr"), ("en", "en") })
            await Apply(pair.Item1, pair.Item2, offline);
        Check(network.Requests == 0, "cached application accessed network");
        Check(before.All(p => File.GetLastWriteTimeUtc(p.Key) == p.Value), "cached packages downloaded again");
        var audio = Path.Combine(game, "data", "Audio");
        Check(!Directory.Exists(audio) || !Directory.GetFiles(audio, "*", SearchOption.AllDirectories).Any(), "English restoration kept translated audio");
        File.WriteAllText(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new { checks, combinations, modReconciliations, offlineRequests = network.Requests }));
        Console.WriteLine($"EUROPEAN LANGUAGES PASS: {checks} checks, {combinations} combinations, {12 + modReconciliations} real reconciliations, zero offline requests.");
    }
}
