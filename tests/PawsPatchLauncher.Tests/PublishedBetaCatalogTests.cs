using System.Text.Json;
using PawsPatchLauncher;

internal static class PublishedBetaCatalogTests
{
    static readonly string[] PatchIds = ["pawpatch-core", "common-ui", "player-colors", "desync-continue",
        "pure-fixes-data", "pure-fixes-runtime", "pure-beta-common"];

    static string Repository()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "src/PawsPatchLauncher/launcher.config.json"))) return d.FullName;
        throw new DirectoryNotFoundException("Repository required for published catalog checks.");
    }

    static ChannelManifest Read(string path, string key)
    {
        var envelope = JsonSerializer.Deserialize(File.ReadAllBytes(path), LauncherJsonContext.Default.SignedFeedEnvelope)!;
        var bytes = Convert.FromBase64String(envelope.Payload);
        if (!CryptoAndIO.VerifySignature(bytes, envelope.Signature, key)) throw new Exception("Invalid feed signature: " + path);
        return JsonSerializer.Deserialize(bytes, LauncherJsonContext.Default.ChannelManifest)!;
    }

    internal static int Run(string? previousPath = null)
    {
        var root = Repository();
        var config = JsonSerializer.Deserialize(File.ReadAllBytes(Path.Combine(root, "src/PawsPatchLauncher/launcher.config.json")),
            LauncherJsonContext.Default.LauncherConfiguration)!;
        var reference = Read(Path.Combine(root, "feed/v2/beta.json"), config.PublicKeyPem);
        var count = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("Published beta catalog: " + reason); count++; }
        foreach (var url in config.BetaFeedUrls)
        {
            var path = new Uri(url).AbsolutePath.Split("/main/", 2)[1];
            var active = Read(Path.Combine(root, path), config.PublicKeyPem);
            foreach (var id in PatchIds)
            {
                var a = reference.Packages.Single(p => p.Id == id); var b = active.Packages.Single(p => p.Id == id);
                Check(a.Version == b.Version && a.Sha256 == b.Sha256 && a.Size == b.Size && a.Urls.SequenceEqual(b.Urls),
                    path + " stale patch " + id + ": " + b.Version + " instead of " + a.Version);
            }
            foreach (var mod in new[] { GameMod.ArcaneWars, GameMod.Vanilla, GameMod.Immortals })
            {
                Check(PawPatchVersions.ForChannel(active, mod) == PawPatchVersions.ForChannel(reference, mod), mod + " advertised version");
                if (previousPath is null) continue;
                var previous = Read(previousPath, config.PublicKeyPem);
                foreach (var lang in new[] { "en", "ru", "de", "fr", "cs", "uk" })
                {
                    var settings = new UserSettings { Mod = mod, Channel = "beta", GameTextLanguage = lang, GameVoiceLanguage = "en" };
                    GameMod.SetPawPatch(settings, true);
                    settings = EffectiveSettings.ForFeed(settings, active);
                    var selected = GamePackageSelector.Select(active, settings, lang == "ru", GameMod.ColorsSelected(settings));
                    var oldSelected = GamePackageSelector.Select(previous, settings, lang == "ru", GameMod.ColorsSelected(settings));
                    var state = new InstallState { AppliedSettings = settings, Modules = oldSelected.ToDictionary(p => p.Id,
                        p => new InstalledModule { Enabled = true, Version = p.Version, ArchiveSha256 = p.Sha256, Priority = p.Priority }) };
                    Check(UpdateDetector.HasRemoteUpdate(state, selected, _ => false), mod + "/" + lang + " update offered");
                    Check(UpdateDetector.HasModuleChanges(state, selected), mod + "/" + lang + " install changes detected");
                    Check(!UpdateDetector.HasRemoteUpdate(state, oldSelected, _ => false), mod + "/" + lang + " old feed reproduces no update");
                }
            }
        }
        Console.WriteLine($"PUBLISHED BETA CATALOG PASS {count}");
        return count;
    }
}
