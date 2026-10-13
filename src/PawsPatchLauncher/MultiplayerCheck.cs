using System.Security.Cryptography;
using System.Text;

namespace PawsPatchLauncher;

public sealed record ReadinessReport(string Fingerprint, int Files, IReadOnlyList<string> Errors, MultiplayerManifest? Details = null);

public static class MultiplayerCheck
{
    public static Dictionary<string, ModuleFile?> Expected(InstallState state)
    {
        var result = new Dictionary<string, ModuleFile?>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in state.Modules.Where(x => x.Value.Enabled).OrderBy(x => x.Value.Priority).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var removal in module.Value.Remove) result[CryptoAndIO.NormalizeRelativePath(removal)] = null;
            foreach (var file in module.Value.Files) result[CryptoAndIO.NormalizeRelativePath(file.Path)] = file;
        }
        return result;
    }

    public static async Task<IReadOnlyList<string>> CriticalAsync(string game, InstallState state, string exe, GameRequirement requirement)
    {
        var errors = new List<string>();
        var vanilla = Path.Combine(game, "k2.exe");
        if (!File.Exists(vanilla)) errors.Add("Missing: k2.exe");
        else if (requirement.K2ExeSha256.Count > 0 && !requirement.K2ExeSha256.Contains(await CryptoAndIO.Sha256Async(vanilla), StringComparer.OrdinalIgnoreCase))
            errors.Add("Unsupported k2.exe: " + requirement.Version);
        if (!File.Exists(exe)) errors.Add("Missing: " + Path.GetFileName(exe));
        foreach (var pair in Expected(state))
        {
            var name = Path.GetFileName(pair.Key);
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) && !pair.Key.StartsWith("startup\\", StringComparison.OrdinalIgnoreCase) && !pair.Key.StartsWith("PawsCustom\\", StringComparison.OrdinalIgnoreCase)) continue;
            var path = PatchRecovery.GamePath(game, pair.Key);
            if (pair.Value is null) { if (File.Exists(path)) errors.Add("Should be removed: " + pair.Key); continue; }
            if (!File.Exists(path)) errors.Add("Missing: " + pair.Key);
            else if (!string.Equals(await CryptoAndIO.Sha256Async(path), pair.Value.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add("Changed: " + pair.Key);
        }
        return errors;
    }

    public static bool IsHotkeyFile(string relative)
    {
        var path = CryptoAndIO.NormalizeRelativePath(relative);
        var parts = path.Split('\\');
        if (parts.Length != 4 || !parts[1].Equals("Localization", StringComparison.OrdinalIgnoreCase)
            || !parts[2].Equals("Hotkeys", StringComparison.OrdinalIgnoreCase)
            || !parts[3].EndsWith(".txt", StringComparison.OrdinalIgnoreCase)) return false;
        return parts[0].Equals("data", StringComparison.OrdinalIgnoreCase)
            || GameLanguages.Choices.Any(code => parts[0].Equals("Local_" + code, StringComparison.OrdinalIgnoreCase)
                || parts[0].Equals("Local_base_" + code, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<ReadinessReport> CreateAsync(string game, InstallState state, UserSettings settings, string executable, string build, CancellationToken ct = default)
    {
        var expected = Expected(state);
        var paths = expected.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        paths.Add("k2.exe");
        paths.Add(Path.GetFileName(executable));
        // Include extra gameplay data left by manual installs; do not traverse launcher backups or junctions.
        var pending = new Stack<string>();
        pending.Push(game);
        while (pending.TryPop(out var folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
                if (Path.GetExtension(file).ToLowerInvariant() is ".tgi" or ".dll" or ".rwd") paths.Add(CryptoAndIO.NormalizeRelativePath(Path.GetRelativePath(game, file)));
            foreach (var directory in Directory.EnumerateDirectories(folder))
                if (!Path.GetFileName(directory).StartsWith('.') && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) pending.Push(directory);
        }
        var errors = new List<string>();
        var details = new MultiplayerManifest { Configuration = ConfigurationCode.Create(settings), GameBuild = build,
            Executable = Path.GetFileName(executable), Modules = state.Modules.Select(x => new MultiplayerModule {
                Id = x.Key, Version = x.Value.Version, Sha256 = x.Value.ArchiveSha256, Enabled = x.Value.Enabled, Priority = x.Value.Priority }).ToList() };
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string line) => digest.AppendData(Encoding.UTF8.GetBytes(line + "\n"));
        Add("PAW-MP1");
        Add(ConfigurationCode.Create(settings));
        Add(build);
        Add(Path.GetFileName(executable).ToLowerInvariant());
        foreach (var module in state.Modules.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            Add($"{module.Key.ToLowerInvariant()}|{module.Value.Version}|{module.Value.ArchiveSha256.ToUpperInvariant()}");
        foreach (var relative in paths.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var path = PatchRecovery.GamePath(game, relative);
            var hash = File.Exists(path) ? await CryptoAndIO.Sha256Async(path, ct) : "MISSING";
            details.Files.Add(new MultiplayerFile { Path = relative, Sha256 = hash });
            Add(relative.ToLowerInvariant() + "|" + hash);
            if (!expected.TryGetValue(relative, out var file)) continue;
            if (file is null ? hash != "MISSING" : !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) errors.Add(relative);
        }
        details.Fingerprint = "PAW-MP1-" + Convert.ToHexString(digest.GetHashAndReset());
        details.IntegrityErrors = errors.ToList();
        return new ReadinessReport(details.Fingerprint, paths.Count, errors, details);
    }
}
