using PawsPatchLauncher;
using System.Text.Json;

internal static class ModModeTests
{
    public static async Task RunAsync(string feedPath, string publicKey, string root)
    {
        root = Path.GetFullPath(root);
        if (Directory.Exists(root)) throw new IOException("Choose a fresh fixture root.");
        Directory.CreateDirectory(root);
        var client = new FeedClient(new LauncherConfiguration { FeedUrls = [Path.GetFullPath(feedPath)],
            PublicKeyPem = File.ReadAllText(publicKey), CacheRoot = Path.Combine(root, "cache") });
        var feed = (await client.GetChannelAsync())!;
        var checks = 0;
        void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; }
        UserSettings Pure() => new() { PawPatchEnabled = false, RussianLocalization = false, CustomPlayerColors = false,
            DesyncMode = "official", IndependentHostility = false, AdditionalRoamingCompanies = false,
            SiegeBalance = false, DisablePowersAndShards = false, RoamingSpawnMode = "standard" };
        var codes = new HashSet<string>();
        foreach (var core in new[] { true, false })
        foreach (var spawn in new[] { "standard", "x2", "x4" })
        for (var bits = 0; bits < 128; bits++)
        {
            var selection = Pure(); selection.PawPatchEnabled = core; selection.RoamingSpawnMode = spawn;
            selection.RussianLocalization = (bits & 1) != 0; selection.CustomPlayerColors = (bits & 2) != 0;
            selection.IndependentHostility = (bits & 4) != 0; selection.DesyncMode = (bits & 8) != 0 ? "continue" : "official";
            selection.AdditionalRoamingCompanies = (bits & 16) != 0; selection.SiegeBalance = (bits & 32) != 0; selection.DisablePowersAndShards = (bits & 64) != 0;
            var active = EffectiveSettings.ForFeed(selection, feed);
            var code = ConfigurationCode.Create(active);
            if (core) Check(codes.Add(code), "Different enabled component selections collided: " + code);
            else
            {
                var expected=Pure();expected.RussianLocalization=selection.RussianLocalization;
                Check(code==ConfigurationCode.Create(expected),"Disabled core retained a dependent option: " + code);
                codes.Add(code);
            }
            Check(ConfigurationCode.Create(ConfigurationCode.Parse(code)) == code, "Code roundtrip: " + code);
            var selected = GamePackageSelector.Select(feed, active, active.RussianLocalization, active.CustomPlayerColors);
            Check(selected.Any(p => p.Id == "pawpatch-core") == core, "Core enabled by an optional dependency: " + code);
            Check(core || selected.All(p => p.Id is "arcane-wars" or "startup-base" or "game-localization-en" or "vanilla-localization-ru" or "menu-runtime" || p.Id.StartsWith("aw-")), "Legacy layer leaked into standalone AW");
            var executable = GameExecutableSelector.Select(new(), active, feed);
            Check(core || !executable.StartsWith("k2_paws_") || executable == "k2_paws_menu_1372.exe", "Core runtime leaked into AW");
        }
        var pure = Pure();
        var pureIds = new List<string> { "arcane-wars", "game-localization-en", "startup-base" };
        if (GameExecutableSelector.HasMenuRuntime(feed)) pureIds.Add("menu-runtime");
        Check(GamePackageSelector.Select(feed, pure, false, false).Select(p => p.Id).Order().SequenceEqual(pureIds.Order()), "Pure AW contains a gameplay layer");
        Check(GameExecutableSelector.Select(new(), pure, feed) == (GameExecutableSelector.HasMenuRuntime(feed) ? "k2_paws_menu_1372.exe" : "k2.exe"), "Pure AW must use only the menu helper when available");
        var vanilla = new UserSettings { Mod = GameMod.Vanilla };
        var activeVanilla = EffectiveSettings.ForFeed(vanilla, feed);
        Check(GamePackageSelector.Select(feed, vanilla, true, true).Select(p => p.Id).SequenceEqual(new[] { "vanilla-localization-ru" }), "Vanilla must install only the chosen language");
        Check(GameExecutableSelector.Select(new(), vanilla, feed) == "k2.exe", "Vanilla launch selection");
        Check(ConfigurationCode.Create(vanilla) == "PAW-STABLE-VANILLA-RU1", "Vanilla language code");
        Check(!activeVanilla.PawPatchEnabled && !activeVanilla.LargeMapSizes && activeVanilla.RussianLocalization && vanilla.PawPatchEnabled && vanilla.RussianLocalization, "Vanilla overwrote remembered options or game language");
        var target = new UserSettings(); ConfigurationCode.Apply(activeVanilla, target);
        Check(target.Mod == GameMod.Vanilla && target.PawPatchEnabled && target.RussianLocalization, "Import erased remembered AW preferences");

        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["k2.exe"] = "original executable fixture", ["startup\\autoexec.txt"] = "original startup fixture",
            ["data\\Game\\world_rules_k2.tgi"] = "original rules fixture", ["Saves\\keep.sav"] = "untouched save",
            ["custom-user-file.txt"] = "unmanaged user file"
        };
        foreach (var (path, value) in originals) { var file = Path.Combine(game, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); File.WriteAllText(file, value); }
        var installer = new ModuleInstaller(game);
        var prepared = new Dictionary<string, InstalledModule>();
        foreach (var package in feed.Packages)
            prepared[package.Id] = await installer.PrepareAsync(package, await client.DownloadVerifiedAsync(package, null));
        var tracked = prepared.Values.SelectMany(m => m.Files).Select(f => CryptoAndIO.NormalizeRelativePath(f.Path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        async Task Apply(UserSettings selection)
        {
            var active = EffectiveSettings.ForFeed(selection, feed);
            var selected = GamePackageSelector.Select(feed, active, active.RussianLocalization, active.CustomPlayerColors);
            await installer.ReconcileAsync(selected.ToDictionary(p => p.Id, p => prepared[p.Id]), settings: active);
            Check(!UpdateDetector.HasSettingsChanges(installer.LoadState(), selected, active), "Applied settings remain pending");
            Check((await installer.VerifyAsync()).Count == 0, "Installed files differ from manifest");
            var expected = MultiplayerCheck.Expected(installer.LoadState());
            foreach (var path in tracked.Where(p => !expected.ContainsKey(p)))
                Check(originals.ContainsKey(path) ? File.ReadAllText(Path.Combine(game,path)) == originals[path] : !File.Exists(Path.Combine(game,path)), "Disabled feature left behind: " + path);
            Check(File.ReadAllText(Path.Combine(game,"Saves/keep.sav")) == "untouched save", "Save changed");
        }
        await Apply(new());
        await Apply(pure);
        var pureHashes = MultiplayerCheck.Expected(installer.LoadState());
        var baseState = new InstallState { Modules = new() { ["arcane-wars"] = prepared["arcane-wars"], ["startup-base"] = prepared["startup-base"], ["game-localization-en"] = prepared["game-localization-en"] } };
        if (prepared.TryGetValue("menu-runtime", out var menu))
        {
            Check(menu.Files.Count == 2 && menu.Files.All(f => f.Path.Equals("k2_paws_menu_1372.exe",StringComparison.OrdinalIgnoreCase)
                || f.Path.Replace('\\','/').Equals("data/UI/Menus/main.tgi",StringComparison.OrdinalIgnoreCase)), "Menu runtime contains unrelated gameplay files");
            baseState.Modules["menu-runtime"] = menu;
        }
        var baseHashes = MultiplayerCheck.Expected(baseState);
        Check(pureHashes.Count == baseHashes.Count && pureHashes.All(p => baseHashes[p.Key]!.Sha256 == p.Value!.Sha256), "Pure AW differs from verified original mod");
        for (int option = 0; option < 7; option++)
        {
            var selection = Pure();
            switch (option) { case 0: selection.RussianLocalization = true; break; case 1: selection.CustomPlayerColors = true; break;
                case 2: selection.IndependentHostility = true; break; case 3: selection.DesyncMode = "continue"; break;
                case 4: selection.AdditionalRoamingCompanies = true; selection.RoamingSpawnMode = "x2"; break;
                case 5: selection.SiegeBalance = true; break; case 6: selection.DisablePowersAndShards = true; break; }
            await Apply(selection); await Apply(pure);
        }
        await Apply(new UserSettings { PawPatchEnabled = false, CustomPlayerColors = true, DesyncMode = "continue" });
        await Apply(new());
        foreach (var language in new[] { true, false })
        foreach (var mode in new[] { GameMod.Immortals, GameMod.Vanilla, GameMod.ArcaneWars, GameMod.Immortals, GameMod.Vanilla })
        {
            var selection = Pure(); selection.Mod = mode; selection.RussianLocalization = language;
            await Apply(selection);
            var applied = installer.LoadState().AppliedSettings!;
            Check(applied.Mod == mode && applied.RussianLocalization == language, "Shared language lost when switching modes");
            Check(GameExecutableSelector.Select(new(), applied, feed) == (mode != GameMod.Vanilla && GameExecutableSelector.HasMenuRuntime(feed) ? "k2_paws_menu_1372.exe" : "k2.exe"), "A pure mod launched gameplay fixes");
            Check(ConfigurationCode.Create(ConfigurationCode.Parse(ConfigurationCode.Create(applied))) == ConfigurationCode.Create(applied), "Mod/language code roundtrip");
        }
        await Apply(new());
        // A conflicting user edit must stop restoration before any file changes.
        var conflict = Path.Combine(game, "startup/autoexec.txt"); var installedStartup = File.ReadAllBytes(conflict);
        File.WriteAllText(conflict, "external edit");
        var protectedRules = await CryptoAndIO.Sha256Async(Path.Combine(game, "data/Game/world_rules_k2.tgi"));
        try { await installer.UninstallAsync(settings: activeVanilla); throw new Exception("External edit was overwritten"); }
        catch (IOException) { Check(File.ReadAllText(conflict) == "external edit", "External edit lost"); }
        Check(await CryptoAndIO.Sha256Async(Path.Combine(game,"data/Game/world_rules_k2.tgi")) == protectedRules, "Partial Vanilla restoration after conflict");
        File.WriteAllBytes(conflict, installedStartup);
        // Historical installations can contain case-only duplicate original records.
        var state = installer.LoadState();
        var duplicate = state.Originals.First(p => !p.Value.Existed);
        state.Originals = new Dictionary<string, OriginalFile>(state.Originals, StringComparer.Ordinal);
        state.Originals[duplicate.Key.ToUpperInvariant()] = duplicate.Value;
        File.WriteAllText(Path.Combine(game,".pawpatch/state.json"),JsonSerializer.Serialize(state,LauncherJsonContext.Default.InstallState));
        await installer.UninstallAsync(settings: activeVanilla);
        Check(installer.LoadState().Modules.Count == 0 && installer.LoadState().AppliedSettings?.Mod == GameMod.Vanilla, "Vanilla state not recorded");
        foreach (var (path, value) in originals) Check(File.ReadAllText(Path.Combine(game,path)) == value, "Original file was not restored: " + path);
        foreach (var path in tracked.Where(p => !originals.ContainsKey(p))) Check(!File.Exists(Path.Combine(game,path)), "Vanilla leftover: " + path);
        await Apply(pure);
        await installer.UninstallAsync(settings: activeVanilla);
        foreach (var (path, value) in originals) Check(File.ReadAllText(Path.Combine(game,path)) == value, "Second restore changed original: " + path);
        Console.WriteLine($"MOD MODES PASS {checks}: 768 selections, disabled-core normalization, signed real packages, three mods and both languages, pure AW hashes, language persistence, external edits and case-only legacy records. No installed game touched.");
    }
}
