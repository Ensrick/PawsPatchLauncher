using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PawsPatchLauncher;

public sealed class ModLibraryDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<ModLibraryEntry> Mods { get; set; } = [];
    public List<PackageRelease> Languages { get; set; } = [];
}

public sealed class ModLibraryEntry
{
    public string Mod { get; set; } = "";
    public string Channel { get; set; } = "stable";
    public string ReleaseId { get; set; } = "";
    public string ContentId { get; set; } = "";
    public List<PackageRelease> Packages { get; set; } = [];
}

// The library retains complete mod releases independently of the files active in the game.
// Release metadata is reloaded from the verified signed archive before it can be applied.
public sealed class ModLibrary(string gameRoot)
{
    private string FilePath => CryptoAndIO.SafeChildPath(Path.Combine(Path.GetFullPath(gameRoot), ".pawpatch"), "mod-library.json");
    private static string Key(string mod, string channel) => mod + ":" + channel;

    public ModLibraryDocument Load()
    {
        if (!File.Exists(FilePath)) return new();
        RemovalSafety.CheckNoLinks(FilePath);
        var library = JsonSerializer.Deserialize(File.ReadAllText(FilePath), LauncherJsonContext.Default.ModLibraryDocument)
            ?? throw new InvalidDataException("The installed mod library is damaged.");
        if (library.SchemaVersion != 1) throw new InvalidDataException("Unsupported mod library version.");
        foreach (var item in library.Mods)
        {
            GameMod.Validate(new UserSettings { Mod = item.Mod });
            if (item.Channel is not ("stable" or "beta") || item.ReleaseId.Length != 64 || !item.ReleaseId.All(Uri.IsHexDigit))
                throw new InvalidDataException("Invalid installed mod release.");
        }
        return library;
    }

    public ModLibraryEntry? Find(string mod, string channel)
        // 0.7.0 could adopt an empty Vanilla entry from an Arcane Wars-only
        // catalog. It is the original game, not an installed component release.
        => Load().Mods.LastOrDefault(item => Key(item.Mod, item.Channel) == Key(mod, channel) && item.Packages.Count > 0);

    public Task RememberAsync(ChannelManifest channel, string mod)
        => Task.Run(() => RememberCoreAsync(channel, mod));

    private async Task RememberCoreAsync(ChannelManifest channel, string mod)
    {
        var packages = Packages(channel, mod);
        if (packages.Count == 0) return;
        var library = Load();
        foreach (var language in library.Mods.SelectMany(m => m.Packages).Where(GameLanguages.IsLanguage))
            if (!library.Languages.Any(p => p.Id == language.Id)) library.Languages.Add(language);
        var entry = new ModLibraryEntry { Mod = mod, Channel = channel.Channel,
            ReleaseId = ChannelFingerprint.Create(channel), ContentId = ContentId(channel, mod), Packages = packages };
        library.Mods.RemoveAll(item => Key(item.Mod, item.Channel) == Key(mod, channel.Channel));
        library.Mods.Add(entry);
        await CryptoAndIO.AtomicWriteTextAsync(FilePath, JsonSerializer.Serialize(library, LauncherJsonContext.Default.ModLibraryDocument));
    }

    public Task RememberLanguagesAsync(IEnumerable<PackageRelease> packages)
        => Task.Run(() => RememberLanguagesCoreAsync(packages));

    private async Task RememberLanguagesCoreAsync(IEnumerable<PackageRelease> packages)
    {
        var library = Load();
        foreach (var package in packages.Where(GameLanguages.IsLanguage))
        {
            library.Languages.RemoveAll(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            library.Languages.Add(package);
        }
        await CryptoAndIO.AtomicWriteTextAsync(FilePath, JsonSerializer.Serialize(library, LauncherJsonContext.Default.ModLibraryDocument));
    }

    public static List<PackageRelease> Packages(ChannelManifest channel, string mod)
    {
        GameMod.Validate(new UserSettings { Mod = mod });
        var packages = channel.Packages.Where(p => BelongsTo(p, mod) && !GameLanguages.IsLanguage(p));
        var result = packages.OrderBy(p => p.Priority).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ToList();
        var ids = result.Select(p => p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (mod != GameMod.Vanilla && !ids.Contains(mod)
            || result.Any(p => p.DependsOn.Any(d => !ids.Contains(d))))
            throw new InvalidDataException("This release does not include all files for the selected mod.");
        return result;
    }

    public static bool BelongsTo(PackageRelease package, string mod)
    {
        if (package.Mods.Count > 0) return package.Mods.Contains(mod, StringComparer.OrdinalIgnoreCase);
        return mod switch
        {
            GameMod.Vanilla => package.Id is "game-localization-en" or "vanilla-localization-ru" or "pure-fixes-data" or "pure-fixes-runtime" or "menu-runtime",
            GameMod.Immortals => package.Id is "immortals" or "startup-base" or "game-localization-en" or "immortals-localization-ru"
                or "immortals-text-fixes" or "pure-fixes-data" or "pure-fixes-runtime" or "menu-runtime",
            _ => package.Id is not "immortals" and not "immortals-localization-ru" and not "immortals-text-fixes"
                and not "pure-fixes-data" and not "pure-fixes-runtime"
        };
    }

    public static string ContentId(ChannelManifest channel, string mod) => ContentId(channel, mod, false);
    public static string LegacyContentId(ChannelManifest channel, string mod) => ContentId(channel, mod, true);
    private static string ContentId(ChannelManifest channel, string mod, bool includeLanguages)
    {
        var packages = includeLanguages ? channel.Packages.Where(p => BelongsTo(p, mod)).ToList() : Packages(channel, mod);
        var text = new StringBuilder(mod);
        if (mod == GameMod.ArcaneWars || packages.Any(p => !p.ExecutableIndependent))
        {
            var game = GameMod.Requirement(channel, mod);
            text.Append('|').Append(game.Version).Append('|').Append(game.SteamBuild)
                .Append('|').AppendJoin(',', game.K2ExeSha256.Order(StringComparer.OrdinalIgnoreCase));
        }
        foreach (var package in packages.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
            text.Append('\n').Append(package.Id).Append('|').Append(package.Version).Append('|').Append(package.Priority)
                .Append('|').Append(package.Sha256.ToUpperInvariant()).Append('|').Append(package.ExecutableIndependent)
                .Append('|').Append(package.Required).Append('|').AppendJoin(',', package.DependsOn.Order(StringComparer.OrdinalIgnoreCase));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static bool HasUpdate(ChannelManifest installed, ChannelManifest offered, string mod)
        => !ContentId(installed, mod).Equals(ContentId(offered, mod), StringComparison.OrdinalIgnoreCase);

    public static bool IsActive(InstallState state, UserSettings selection)
        => state.AppliedSettings?.Mod == selection.Mod
            && state.AppliedSettings.Channel == selection.Channel;
}
