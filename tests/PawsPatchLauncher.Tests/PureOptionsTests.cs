using System.Text.Json;
using PawsPatchLauncher;

internal static class PureOptionsTests
{
    internal static int Run()
    {
        int checks = 0;
        void Check(bool ok, string why) { checks++; if (!ok) throw new Exception("Pure options: " + why); }
        foreach (string channel in new[] { "stable", "beta" })
        {
        var beta = new ChannelManifest { Channel = channel, PureRuntimeOptions = true,
            Packages = [new() { Id = "pure-fixes-data" }, new() { Id = "pure-fixes-runtime", ExecutableIndependent = false },
                new() { Id = "pure-player-colors", Mods = ["vanilla", "immortals"], DependsOn = ["pure-fixes-runtime"] },
                new() { Id = "immortals", ExecutableIndependent = true }, new() { Id = "startup-base", ExecutableIndependent = true }] };
        beta.Packages[0].ExecutableIndependent = true;
        foreach (string mod in new[] { "vanilla", "immortals" })
        foreach (bool colors in new[] { false, true })
        foreach (bool sync in new[] { false, true })
        foreach (bool data in new[] { false, true })
        foreach (bool patch in new[] { false, true })
        {
            var prefs = new UserSettings { Mod = mod, Channel = channel, DataOnly = data, RussianLocalization = false,
                CustomPlayerColors = true, DesyncMode = "continue" };
            GameMod.SetPawPatch(prefs, true); GameMod.SetColors(prefs, colors); GameMod.SetDesync(prefs, sync);
            if (!patch) GameMod.SetPawPatch(prefs, false);
            var active = EffectiveSettings.ForFeed(prefs, beta);
            Check(active.CustomPlayerColors == (colors && patch && !data), "color gating");
            Check((active.DesyncMode == "continue") == (sync && patch && !data), "sync gating");
            Check(prefs.CustomPlayerColors && prefs.DesyncMode == "continue", "pure selection mutated AW preferences");
            var code = ConfigurationCode.Create(active);
            var packages = GamePackageSelector.Select(beta, active, false, active.CustomPlayerColors);
            Check(packages.Any(p => p.Id == "pure-player-colors") == (colors && patch && !data), "color package");
            Check(!data || packages.All(p => p.ExecutableIndependent), "data-only native package");
            string exe = GameExecutableSelector.Select(new(), active, beta);
            Check(exe == (patch && !data ? GameExecutableSelector.PureExecutable(active) : "k2.exe"), "runtime selection");
            if (patch)
            {
                GameMod.SetPawPatch(prefs, false);
                Check(!GameMod.ColorsSelected(prefs) && !GameMod.DesyncSelected(prefs), "master off clears switches");
                GameMod.SetPawPatch(prefs, true);
                Check(GameMod.ColorsSelected(prefs) == colors && GameMod.DesyncSelected(prefs) == sync, "master restores own preferences");
            }
            var other = new UserSettings { Mod = mod == "vanilla" ? "immortals" : "vanilla", CustomPlayerColors = false };
            GameMod.SetPawPatch(other, true); GameMod.SetColors(other, true);
            ConfigurationCode.Apply(ConfigurationCode.Parse(code), other);
            Check(ConfigurationCode.Create(EffectiveSettings.ForFeed(other, beta)) == code, "import exact feature choices");
            Check(!other.CustomPlayerColors, "copy modified remembered AW colors");
            var old = new ChannelManifest { Channel = channel, Packages = beta.Packages };
            var masked = EffectiveSettings.ForFeed(prefs, old);
            Check(!masked.CustomPlayerColors && masked.DesyncMode == "official", "old feed must not expose new runtime options");
        }
        }
        Console.WriteLine("PURE OPTIONS PASS " + checks + ": all feature/master/data combinations, mod isolation, imports and legacy feeds");
        return checks;
    }

    // Explicit maintenance entrypoint: installs through the same package selector,
    // compatibility gate and transactional installer used by the launcher. Never launches.
    internal static async Task StageAsync(string configPath, string root, string selectionPath)
    {
        if (System.Diagnostics.Process.GetProcessesByName("k2").Any()) throw new IOException("Close game before staging");
        var config = JsonSerializer.Deserialize(File.ReadAllText(configPath), LauncherJsonContext.Default.LauncherConfiguration)!;
        config.CacheRoot = Path.Combine(Path.GetDirectoryName(configPath)!, "cache");
        var client = new FeedClient(config);
        var s = JsonSerializer.Deserialize(File.ReadAllText(selectionPath), LauncherJsonContext.Default.UserSettings)!;
        if (s.Mod is not (GameMod.Vanilla or GameMod.Immortals)) throw new IOException("Pure modes only");
        var feed = await client.GetChannelAsync(s.Channel) ?? throw new IOException("No feed");
        var hash = await CryptoAndIO.Sha256Async(Path.Combine(root, "k2.exe"));
        var requirement = GameMod.Requirement(feed, s.Mod);
        s.DataOnly = s.DataOnly || !GameCompatibilityPolicy.Supports(requirement, hash);
        s = EffectiveSettings.ForFeed(s, feed);
        var installer = new ModuleInstaller(root); var modules = new Dictionary<string, InstalledModule>();
        foreach (var p in GamePackageSelector.Select(feed, s, s.RussianLocalization, s.CustomPlayerColors))
            modules[p.Id] = installer.IsPrepared(p) ? await installer.ReadPreparedAsync(p) : await installer.PrepareAsync(p, await client.DownloadVerifiedAsync(p, null));
        await installer.ReconcileAsync(modules, settings:s, releaseId:ChannelFingerprint.Create(feed), gameRequirement:requirement, baseGameSha256:hash);
        if (!s.DataOnly) await GameMenuMetadata.WriteAsync(root, feed, s);
        var errors = await installer.VerifyAsync(); if (errors.Count != 0) throw new IOException(string.Join(";", errors));
        await GameLobbyPreferences.PrepareForLaunchAsync(root, s);
        Console.WriteLine("PURE_STAGE_PASS " + ConfigurationCode.Create(s) + " executable=" + GameExecutableSelector.Select(config, s, feed));
    }
}
