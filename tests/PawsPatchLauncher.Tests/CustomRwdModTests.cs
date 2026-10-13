using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PawsPatchLauncher;
using PawsPatchLauncher.Rwd;

internal static class CustomRwdModTests
{
    public static async Task VerifyInstalledAsync(string game, string output)
    {
        if (!File.Exists(Path.Combine(game, ".paw-live-matrix.json"))) throw new InvalidOperationException("Private matrix fixture required.");
        Directory.CreateDirectory(output);
        var source = Path.Combine(output, "identity-check.rwd");
        RwdArchive.Write(source, new Dictionary<string, byte[]> { ["paws-qa/unused.txt"] = [1] });
        var installer = new ModuleInstaller(game); var before = installer.LoadState();
        var profiles = CustomRwdMods.Copy(before.CustomMods);
        profiles[before.AppliedSettings!.Mod] = [new CustomRwdMods(game).Import(source) with { Enabled = true }];
        try
        {
            await installer.ReconcileAsync(before.Modules, settings: before.AppliedSettings, customMods: profiles);
            foreach (var helper in Directory.GetFiles(game, "k2_paws_pure_*.exe"))
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(helper)
                { ArgumentList = { "--preflight", game }, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
                string text = await process.StandardOutput.ReadToEndAsync(); string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0 || !text.Contains("PURE_PREFLIGHT_PASS")) throw new IOException(error + text);
                Console.WriteLine("CUSTOM_RWD_NATIVE_PREFLIGHT_PASS " + Path.GetFileName(helper));
            }
        }
        finally { await installer.ReconcileAsync(before.Modules, settings: before.AppliedSettings, customMods: before.CustomMods); }
    }
    public static async Task StageAsync(string game, string a, string b, bool enabled)
    {
        if (!File.Exists(Path.Combine(game, "rwd-engine-fixture.marker"))) throw new InvalidOperationException("Private fixture marker required.");
        var library = new CustomRwdMods(game);
        var profiles = new Dictionary<string, List<CustomRwdMod>> { [GameMod.Vanilla] = [library.Import(a) with { Enabled = enabled }, library.Import(b) with { Enabled = enabled }] };
        await new ModuleInstaller(game).ReconcileAsync(new Dictionary<string, InstalledModule>(),
            settings: new UserSettings { Mod = GameMod.Vanilla }, customMods: profiles);
        Console.WriteLine("MANAGED RWD FIXTURE " + (enabled ? "ON" : "OFF"));
    }
    public static async Task<int> RunAsync(string root)
    {
        int passed = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Custom RWD: " + why); passed++; }
        async Task Reject(Func<Task> action, string why)
        { try { await action(); } catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException or OperationCanceledException) { passed++; return; } throw new Exception("Custom RWD should reject: " + why); }
        var game = Path.Combine(root, "custom-rwd"); Directory.CreateDirectory(Path.Combine(game, "startup"));
        var bootstrap = Encoding.UTF8.GetBytes("# preserve exactly\r\nadddepot Data.rwd\r\nadddepot data/\r\nadddepot %USERDATA%/data/ 1\r\n");
        var startup = Path.Combine(game, "startup", "autoexec.txt"); await File.WriteAllBytesAsync(startup, bootstrap);
        await File.WriteAllTextAsync(Path.Combine(game, "k2.exe"), "fixture");
        var installer = new ModuleInstaller(game); var library = new CustomRwdMods(game);
        var settings = new UserSettings { Mod = GameMod.Vanilla };
        var normal = new Dictionary<string, InstalledModule>();
        async Task Apply(Dictionary<string, List<CustomRwdMod>> profiles, Action? guard = null)
        {
            var state = installer.LoadState();
            await installer.ReconcileAsync(state.Modules, settings: settings, customMods: profiles,
                expectedState: JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), ensureGameClosed: guard);
        }
        var sourceA = Path.Combine(root, "Named A.rwd"); var sourceB = Path.Combine(root, "Другой мод.rwd");
        RwdArchive.Write(sourceA, new Dictionary<string, byte[]> { ["test/sample.tgi"] = [1, 2, 3] });
        RwdArchive.Write(sourceB, new Dictionary<string, byte[]> { ["test/sample.tgi"] = [4, 5, 6] });
        var a = library.Import(sourceA); var b = library.Import(sourceB);
        if (OperatingSystem.IsWindows())
        {
            var alias = Path.Combine(root, "relocated mod downloads");
            var originals = Path.Combine(root, "physical mod downloads"); Directory.CreateDirectory(originals);
            File.Copy(sourceA, Path.Combine(originals, "Named A.rwd"));
            using (RwdRelocatedPathsTests.Junction(alias, originals))
            {
                var imported = library.Import(Path.Combine(alias, "Named A.rwd"));
                Check(imported == a, "import through a relocated source folder keeps archive identity");
                Check(File.Exists(Path.Combine(originals, "Named A.rwd")), "relocated import keeps the source");
                await File.WriteAllTextAsync(Path.Combine(originals, "broken.rwd"), "invalid");
                await Reject(() => { library.Import(Path.Combine(alias, "broken.rwd")); return Task.CompletedTask; }, "invalid relocated archive");
            }
            var linkedGame = Path.Combine(root, "cache-link-game"); Directory.CreateDirectory(linkedGame);
            var cacheTarget = Path.Combine(root, "cache-link-target"); Directory.CreateDirectory(cacheTarget);
            var marker = Path.Combine(cacheTarget, "sentinel.txt"); await File.WriteAllTextAsync(marker, "sentinel");
            using (RwdRelocatedPathsTests.Junction(Path.Combine(linkedGame, ".pawpatch"), cacheTarget))
            {
                await Reject(() => { new CustomRwdMods(linkedGame).Import(sourceA); return Task.CompletedTask; }, "link in managed cache");
                Check(Directory.GetFiles(cacheTarget).Length == 1 && await File.ReadAllTextAsync(marker) == "sentinel", "cache link cannot redirect an import write");
            }
        }
        Check(!a.Enabled && !b.Enabled, "import requires explicit enable");
        Check(a.Files == 1 && a.Name == "Named A" && b.Name == "Другой мод", "names and counts");
        Check(File.Exists(sourceA) && File.Exists(sourceB), "sources preserved");
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(bootstrap), "import does not mount");
        await Reject(() => { library.Import(startup); return Task.CompletedTask; }, "non archive");
        var bad = Path.Combine(root, "invalid.rwd"); await File.WriteAllTextAsync(bad, "invalid");
        await Reject(() => { library.Import(bad); return Task.CompletedTask; }, "invalid archive");
        var duplicate = new Dictionary<string, List<CustomRwdMod>> { [GameMod.Vanilla] = [a, a] };
        await Reject(() => Apply(duplicate), "duplicate archive");
        var profiles = new Dictionary<string, List<CustomRwdMod>> { [GameMod.Vanilla] = [a with { Enabled = true }, b with { Enabled = true }] };
        await Apply(profiles);
        var text = await File.ReadAllTextAsync(startup);
        Check(text.StartsWith(Encoding.UTF8.GetString(bootstrap)), "original bytes retained");
        Check(text.IndexOf(a.Sha256.ToLowerInvariant()) < text.IndexOf(b.Sha256.ToLowerInvariant()), "ordered mounts");
        Check(text.Contains("PawsCustom/") && !text.Contains("Другой"), "safe generated paths");
        Check((await installer.VerifyAsync()).Count == 0, "installed verification");
        Check(!UpdateDetector.HasModuleChanges(installer.LoadState(), []), "local mods do not create endless update prompt");
        var first = installer.LoadState().Modules[CustomRwdMods.ModuleId].Version;
        Check(first.Length <= 48 && installer.LoadState().Modules[CustomRwdMods.ModuleId].ArchiveSha256.Length == 64,
            "native helper version-field contract with full archive digest retained");
        await Apply(profiles);
        Check(installer.LoadState().Modules[CustomRwdMods.ModuleId].Version == first, "idempotent apply");
        Check((await File.ReadAllTextAsync(startup)).Split("local RWD mods").Length == 2, "no repeated mount suffix");
        profiles[GameMod.Vanilla].Reverse(); await Apply(profiles);
        text = await File.ReadAllTextAsync(startup);
        Check(text.IndexOf(b.Sha256.ToLowerInvariant()) < text.IndexOf(a.Sha256.ToLowerInvariant()), "reorder");
        Check(installer.LoadState().Modules[CustomRwdMods.ModuleId].Version != first, "order part of module identity");
        await new PatchRecovery(game).RollbackAsync(installer.LoadState());
        Check(installer.LoadState().Modules[CustomRwdMods.ModuleId].Version == first, "rollback restores mounts and identity");
        Check(installer.LoadState().CustomMods[GameMod.Vanilla][0].Sha256 == a.Sha256, "rollback restores selection");
        profiles[GameMod.Vanilla] = [a, b]; await Apply(profiles);
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(bootstrap), "disable all restores exact original");
        Check(!installer.LoadState().Modules.ContainsKey(CustomRwdMods.ModuleId), "disabled not in integrity manifest");
        Check(!Directory.EnumerateFiles(Path.Combine(game, "PawsCustom")).Any(), "disabled archives unmounted and removed from active directory");
        Check(File.Exists(sourceA), "disable leaves original archive");
        profiles[GameMod.Vanilla] = [a with { Enabled = true }]; await Apply(profiles);
        settings.Mod = GameMod.Immortals;
        await installer.ReconcileAsync(normal, settings: settings);
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(bootstrap), "different game does not inherit mods");
        settings.Mod = GameMod.Vanilla;
        await installer.ReconcileAsync(normal, settings: settings);
        Check(installer.LoadState().Modules.ContainsKey(CustomRwdMods.ModuleId), "return to game restores its mods");
        var before = await File.ReadAllBytesAsync(startup); var beforeState = await File.ReadAllBytesAsync(Path.Combine(game, ".pawpatch", "state.json"));
        int guards = 0;
        profiles[GameMod.Vanilla] = [b with { Enabled = true }];
        await Reject(() => Apply(profiles, () => { if (++guards == 3) throw new IOException("simulated game start during transaction"); }), "mid-install interruption");
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(before), "failed change restores autoexec");
        Check((await File.ReadAllBytesAsync(Path.Combine(game, ".pawpatch", "state.json"))).SequenceEqual(beforeState), "failed change restores state");
        Check((await installer.VerifyAsync()).Count == 0, "rollback verifies");
        await Reject(() => installer.ReconcileAsync(normal, settings: settings, customMods: profiles, expectedState: "stale"), "stale window");
        var activeArchive = Path.Combine(game, "PawsCustom", a.Sha256.ToLowerInvariant() + ".rwd");
        await File.WriteAllTextAsync(activeArchive, "tampered");
        Check((await MultiplayerCheck.CriticalAsync(game, installer.LoadState(), Path.Combine(game, "k2.exe"), new GameRequirement())).Any(e => e.Contains("PawsCustom")), "changed active RWD blocks launch");
        await Apply(new() { [GameMod.Vanilla] = [a with { Enabled = true }] });
        var cached = Path.Combine(game, ".pawpatch", "custom-rwd", b.Sha256.ToLowerInvariant() + ".rwd");
        await File.WriteAllTextAsync(cached, "tampered");
        await Reject(() => Apply(profiles), "changed cached RWD");
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(before), "bad cache leaves installed mounts");
        profiles[GameMod.Vanilla] = []; await Apply(profiles);
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(bootstrap), "remove last mod restores original");
        Check(installer.LoadState().CustomMods[GameMod.Vanilla].Count == 0, "removal persisted");
        await Apply(new() { [GameMod.Vanilla] = [a with { Enabled = true }] });
        await installer.UninstallAsync();
        Check((await File.ReadAllBytesAsync(startup)).SequenceEqual(bootstrap), "full uninstall restores original");
        Check(!installer.LoadState().Modules.ContainsKey(CustomRwdMods.ModuleId), "uninstall cannot re-enable local mods");
        // Normal package updates supply a new bootstrap, while a mod-only change preserves personal key bindings.
        const string officialId = "test-core";
        async Task<InstalledModule> Official(string version)
        {
            var payload = Path.Combine(game, ".pawpatch", "packages", officialId, version, "payload");
            var module = new InstalledModule { Version = version, Enabled = true, Priority = 10, ArchiveSha256 = new string('A', 64) };
            foreach (var (path, content) in new[] { ("startup/autoexec.txt", "# " + version + "\nadddepot data.rwd\n"), ("Local_ru/Localization/Hotkeys/custom.txt", "original keys"), ("Local_ru/Localization/strings.tgi", "localized strings") })
            {
                var file = Path.Combine(payload, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!); await File.WriteAllTextAsync(file, content);
                module.Files.Add(new ModuleFile { Path = path, Size = new FileInfo(file).Length, Sha256 = await CryptoAndIO.Sha256Async(file) });
            }
            return module;
        }
        normal[officialId] = await Official("v1");
        await installer.ReconcileAsync(normal, settings: settings);
        var keys = Path.Combine(game, "Local_ru/Localization/Hotkeys/custom.txt"); await File.WriteAllTextAsync(keys, "my keys");
        await Apply(new() { [GameMod.Vanilla] = [a with { Enabled = true }] });
        Check(await File.ReadAllTextAsync(keys) == "my keys", "mod enable preserves custom bindings");
        Check((await installer.VerifyAsync(allowCustomHotkeys: true)).Count == 0, "bindings do not block readiness");
        Check((await File.ReadAllTextAsync(startup)).StartsWith("# v1"), "uses effective official startup");
        await Apply(new() { [GameMod.Vanilla] = [a] });
        Check(await File.ReadAllTextAsync(keys) == "my keys", "mod disable preserves custom bindings");
        Check(await File.ReadAllTextAsync(startup) == "# v1\nadddepot data.rwd\n", "disable restores official overlay");
        Check(await File.ReadAllTextAsync(Path.Combine(game, "Local_ru/Localization/strings.tgi")) == "localized strings", "mod changes preserve localization");
        await Apply(new() { [GameMod.Vanilla] = [a with { Enabled = true }] });
        normal[officialId] = await Official("v2");
        await installer.ReconcileAsync(normal, settings: settings);
        Check((await File.ReadAllTextAsync(startup)).StartsWith("# v2") && (await File.ReadAllTextAsync(startup)).Contains("PawsCustom/"), "official update rebases mounts");
        Check((await installer.VerifyAsync()).Count == 0, "rebased package verification");
        await Apply(new() { [GameMod.Vanilla] = [] });
        Check(await File.ReadAllTextAsync(startup) == "# v2\nadddepot data.rwd\n", "disable after update restores newest startup");
        await Reject(() => Apply(new() { ["../bad"] = [a] }), "unknown profile");
        await Reject(() => Apply(new() { [GameMod.Vanilla] = [a with { Sha256 = "../escape" }] }), "unsafe archive identifier");
        return passed;
    }
}
