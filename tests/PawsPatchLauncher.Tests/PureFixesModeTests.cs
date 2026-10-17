using System.Text.Json;
using PawsPatchLauncher;

internal static class PureFixesModeTests
{
    public static int Run()
    {
        var count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("Pure fixes: " + message); count++; }
        ChannelManifest Clone(ChannelManifest source) => JsonSerializer.Deserialize(JsonSerializer.Serialize(source,
            LauncherJsonContext.Default.ChannelManifest), LauncherJsonContext.Default.ChannelManifest)!;
        PackageRelease Package(string id, string[] mods, bool independent = true) => new()
        {
            Id = id, Version = "1.0.0", Mods = [.. mods], ExecutableIndependent = independent,
            Sha256 = new string('A', 64), Priority = 100
        };
        var modNames = new[] { GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars };
        var channel = new ChannelManifest { Game = new() { K2ExeSha256 = [new string('B', 64)] } };
        foreach (var mod in modNames)
            channel.ModGames[mod] = new() { K2ExeSha256 = [new string('B', 64)] };
        channel.Packages =
        [
            Package("arcane-wars", [GameMod.ArcaneWars]),
            Package("pawpatch-core", [GameMod.ArcaneWars], false),
            Package("startup-base", [GameMod.ArcaneWars, GameMod.Immortals]),
            Package("game-localization-en", modNames),
            Package("vanilla-localization-ru", [GameMod.Vanilla, GameMod.ArcaneWars]),
            Package("aw-localization-ru", [GameMod.ArcaneWars]),
            Package("immortals", [GameMod.Immortals]),
            Package("immortals-localization-ru", [GameMod.Immortals]),
            Package("immortals-text-fixes", [GameMod.Immortals]),
            Package("pure-fixes-data", [GameMod.Vanilla, GameMod.Immortals]),
            Package("pure-fixes-runtime", [GameMod.Vanilla, GameMod.Immortals], false),
            Package("future-vanilla-fix", [GameMod.Vanilla]),
            Package("future-immortals-fix", [GameMod.Immortals]),
            Package("future-arcane-fix", [GameMod.ArcaneWars])
        ];
        channel.Packages.Single(p => p.Id == "pawpatch-core").Required = true;
        foreach (var changed in channel.Packages)
        {
            var offered = Clone(channel);
            offered.Packages.Single(p => p.Id == changed.Id).Version = "2.0.0";
            foreach (var activeMod in modNames)
                Check(ModLibrary.HasUpdate(channel, offered, activeMod) == (changed.Mods.Contains(activeMod) && !GameLanguages.IsLanguage(changed)),
                    changed.Id + " update leaked into " + activeMod);
        }
        foreach (var changedMod in modNames)
        {
            var offered = Clone(channel);
            offered.ModGames[changedMod].K2ExeSha256 = [new string('C', 64)];
            foreach (var activeMod in modNames)
                Check(ModLibrary.HasUpdate(channel, offered, activeMod) == (activeMod == changedMod), "game requirement crossed mod boundary");
        }
        channel.Changelog = [new() { Category = "patch", Version = "AW-old", PublishedAt = "2026-09-01" },
            new() { Category = "launcher", Version = "launcher-shared", PublishedAt = "2026-09-01" }];
        foreach (var mod in modNames)
        {
            var preferences = new UserSettings { Mod = mod };
            Check(ChangelogReadState.IsUnread(preferences, channel, "patch") == (mod == GameMod.ArcaneWars), "old AW history notification leaked");
            Check(ChangelogReadState.IsUnread(preferences, channel, "launcher"), "launcher history must remain shared");
        }
        foreach (var mod in modNames)
            channel.Changelog.Add(new() { Category = "patch", Version = mod + "-fix", PublishedAt = "2026-09-10", Mods = [mod] });
        var reader = new UserSettings { Mod = GameMod.Vanilla };
        ChangelogReadState.MarkViewed(reader, channel, "patch", true);
        Check(!ChangelogReadState.IsUnread(reader, channel, "patch"), "read Vanilla news stayed unread");
        foreach (var mod in new[] { GameMod.Immortals, GameMod.ArcaneWars })
        {
            reader.Mod = mod;
            Check(ChangelogReadState.IsUnread(reader, channel, "patch"), "viewing Vanilla history marked another mod read");
        }
        var arcaneGameChange = Clone(channel);
        arcaneGameChange.ModGames.Remove(GameMod.ArcaneWars);
        var arcaneGameBefore = Clone(arcaneGameChange);
        arcaneGameChange.Game.K2ExeSha256 = [new string('D', 64)];
        foreach (var activeMod in modNames)
            Check(ModLibrary.HasUpdate(arcaneGameBefore, arcaneGameChange, activeMod) == (activeMod == GameMod.ArcaneWars), "legacy AW requirement affected another mod");

        foreach (var mod in new[] { GameMod.Vanilla, GameMod.Immortals })
        foreach (var enabled in new[] { false, true })
        foreach (var russian in new[] { false, true })
        foreach (var dataOnly in new[] { false, true })
        {
            var settings = new UserSettings { Mod = mod, RussianLocalization = russian, DataOnly = dataOnly,
                PawPatchEnabled = true, CustomPlayerColors = true, DesyncMode = "continue" };
            GameMod.SetPawPatch(settings, enabled);
            var effective = EffectiveSettings.ForFeed(settings, channel);
            var packages = GamePackageSelector.Select(channel, effective, russian, effective.CustomPlayerColors);
            Check(settings.PawPatchEnabled && settings.CustomPlayerColors && settings.DesyncMode == "continue", "remembered AW preferences changed");
            Check(effective.PawPatchEnabled == enabled && !effective.LargeMapSizes && !effective.CustomPlayerColors
                && !effective.IndependentHostility && !effective.SiegeBalance && effective.DesyncMode == "official", "AW rules leaked into pure fixes");
            Check(packages.Any(p => p.Id == "pure-fixes-data") == enabled, "badge fix selection");
            Check(packages.Any(p => p.Id == "pure-fixes-runtime") == (enabled && !dataOnly), "unsupported runtime selected");
            Check(packages.Any(p => p.Id == "immortals-text-fixes") == (enabled && mod == GameMod.Immortals), "Immortals labels leaked");
            Check(packages.All(p => ModLibrary.BelongsTo(p, mod)), "foreign mod package selected");
            Check(!effective.DataOnly || packages.All(p => p.ExecutableIndependent), "native package in file-only mode");
            Check(GameExecutableSelector.Select(new(), effective, channel) == (enabled && !dataOnly ? "k2_paws_pure_fixes_1372.exe" : "k2.exe"), "wrong game executable");
            var code = ConfigurationCode.Create(effective);
            Check(ConfigurationCode.Create(ConfigurationCode.Parse(code)) == code, "configuration code does not roundtrip: " + code);
            count++;
            var target = new UserSettings { PawPatchEnabled = true };
            ConfigurationCode.Apply(effective, target);
            Check(GameMod.PawPatchSelected(target) == enabled && target.PawPatchEnabled, "import lost patch selection or AW preference");
            var otherMod = mod == GameMod.Vanilla ? GameMod.Immortals : GameMod.Vanilla;
            target.Mod = otherMod;
            Check(!GameMod.PawPatchSelected(target), "one mod toggle changed another mod");
        }
        var old = Clone(channel);
        old.Packages.RemoveAll(p => p.Id.StartsWith("pure-fixes-"));
        foreach (var mod in new[] { GameMod.Vanilla, GameMod.Immortals })
        {
            var preferences = new UserSettings { Mod = mod }; GameMod.SetPawPatch(preferences, true);
            var effective = EffectiveSettings.ForFeed(preferences, old);
            Check(!effective.PawPatchEnabled && GameMod.PawPatchSelected(preferences), "old feed forgot selected fixes");
            Check(GameExecutableSelector.Select(new(), effective, old) == "k2.exe", "old release tried to start missing runtime");
        }
        Console.WriteLine($"PURE FIXES MODE PASS {count}: all mod update directions, package scopes, independent compatibility, optional fixes, languages and configuration roundtrips");
        return count;
    }
}
