using System.Security.Cryptography;
using System.Text;
using PawsPatchLauncher.Rwd;

namespace PawsPatchLauncher;

public sealed record CustomRwdMod(string Name, string Sha256, long Size, int Files, bool Enabled = false);

/// <summary>Local RWD resources; no DLLs, executable replacement, or changes to shared configuration codes.</summary>
public sealed class CustomRwdMods(string gameRoot)
{
    public const string ModuleId = "local-custom-rwd";
    public const int MaximumMods = 32;
    public const long MaximumArchive = 2L * 1024 * 1024 * 1024;
    const string Startup = "startup\\autoexec.txt";
    readonly string game = Path.GetFullPath(gameRoot);

    public static void Validate(IReadOnlyDictionary<string, List<CustomRwdMod>> profiles)
    {
        if (profiles is null || profiles.Count > 3) throw new InvalidDataException("Invalid custom mod profiles.");
        foreach (var (profile, mods) in profiles)
        {
            if (profile is not (GameMod.Vanilla or GameMod.Immortals or GameMod.ArcaneWars) || mods is null || mods.Count > MaximumMods)
                throw new InvalidDataException("Invalid custom mod profile.");
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in mods)
            {
                if (mod is null || string.IsNullOrWhiteSpace(mod.Name) || mod.Name.Length > 120 || mod.Name.Any(char.IsControl)
                    || mod.Sha256 is null || mod.Sha256.Length != 64 || !mod.Sha256.All(Uri.IsHexDigit)
                    || !hashes.Add(mod.Sha256) || mod.Size < 1 || mod.Size > MaximumArchive || mod.Files < 1 || mod.Files > RwdArchive.MaxEntries)
                    throw new InvalidDataException("Invalid custom mod entry.");
            }
        }
    }

    public static Dictionary<string, List<CustomRwdMod>> Copy(IReadOnlyDictionary<string, List<CustomRwdMod>> profiles)
    {
        Validate(profiles);
        return profiles.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
    }

    string Cache(string hash) => Path.Combine(game, ".pawpatch", "custom-rwd", hash.ToLowerInvariant() + ".rwd");
    static string Mounted(string hash) => "PawsCustom/" + hash.ToLowerInvariant() + ".rwd";

    // Resource names of a stored archive, for the load-order view only. Installation never relies on this list.
    public HashSet<string> Resources(string sha256)
    {
        if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit)) throw new ArgumentException("Invalid archive digest.", nameof(sha256));
        var path = Cache(sha256);
        RemovalSafety.CheckNoLinks(path);
        using var archive = RwdArchive.Open(path);
        return archive.Entries.Select(e => e.Name.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// For each mod in load order, the earlier mods whose resources it replaces and how many. A null entry
    /// (disabled or unreadable mod) neither replaces nor is replaced, because only enabled mods are mounted.
    /// </summary>
    public static List<(int Earlier, int Files)>[] Overrides(IReadOnlyList<IReadOnlySet<string>?> resources)
    {
        var result = new List<(int Earlier, int Files)>[resources.Count];
        for (int later = 0; later < resources.Count; later++)
        {
            result[later] = [];
            if (resources[later] is not { } names) continue;
            for (int earlier = 0; earlier < later; earlier++)
                if (resources[earlier] is { } replaced && names.Count(replaced.Contains) is > 0 and var files)
                    result[later].Add((earlier, files));
        }
        return result;
    }

    // Copy first, then validate that exact immutable copy. Source replacement cannot change the imported content.
    public CustomRwdMod Import(string path, CancellationToken ct = default)
    {
        if (!Path.GetExtension(path).Equals(".rwd", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Choose an RWD archive.");
        // The user-selected source is read through one file handle and copied
        // before validation. Its parent may be a relocated Downloads/Steam
        // directory; no writes or removals are performed through that path.
        var cache = Path.GetDirectoryName(Cache(new string('0', 64)))!;
        RemovalSafety.CheckNoLinks(cache);
        Directory.CreateDirectory(cache);
        var temporary = Path.Combine(cache, Guid.NewGuid().ToString("N") + ".import");
        try
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                if (input.Length > MaximumArchive) throw new InvalidDataException("RWD archive exceeds 2 GiB.");
                var buffer = new byte[65536];
                int count;
                while ((count = input.Read(buffer)) != 0) { ct.ThrowIfCancellationRequested(); output.Write(buffer, 0, count); }
            }
            ct.ThrowIfCancellationRequested();
            int files;
            using (var archive = RwdArchive.Open(temporary))
            {
                files = archive.Entries.Count;
                if (files == 0 || archive.Entries.Any(e => e.Name.Length > 240))
                    throw new InvalidDataException("Empty archive or a resource path too long for the game.");
            }
            string hash;
            using (var data = File.OpenRead(temporary)) hash = Convert.ToHexString(SHA256.HashData(data));
            var name = Path.GetFileNameWithoutExtension(path);
            var mod = new CustomRwdMod(name, hash, new FileInfo(temporary).Length, files);
            Validate(new Dictionary<string, List<CustomRwdMod>> { [GameMod.Vanilla] = [mod] });
            var destination = Cache(hash);
            RemovalSafety.CheckNoLinks(destination);
            if (File.Exists(destination))
            {
                using var existing = File.OpenRead(destination);
                if (!Convert.ToHexString(SHA256.HashData(existing)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The stored mod archive has changed.");
            }
            else File.Move(temporary, destination);
            return mod;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    // Earlier entries are mounted first. Actual 1.3.72 engine tests establish that the last mount wins.
    // All generated files participate in the existing installer transaction, verification and rollback.
    public async Task<Dictionary<string, InstalledModule>> ComposeAsync(
        IReadOnlyDictionary<string, InstalledModule> desired, InstallState previous,
        IReadOnlyDictionary<string, List<CustomRwdMod>> profiles, string? activeMod, CancellationToken ct)
    {
        Validate(profiles);
        var result = desired.Where(p => p.Key != ModuleId).ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase);
        var enabled = activeMod is not null && profiles.TryGetValue(activeMod, out var mods) ? mods.Where(m => m.Enabled).ToList() : [];
        if (enabled.Count == 0) return result;

        var baseBytes = await ReadBaseStartupAsync(result, previous, ct);
        // Preserve BOM, encoding and every original byte; generated mount paths are ASCII and contain no user names.
        var unicode = baseBytes.Length >= 2 && baseBytes[0] == 0xff && baseBytes[1] == 0xfe;
        if (baseBytes.Length >= 2 && baseBytes[0] == 0xfe && baseBytes[1] == 0xff)
            throw new InvalidDataException("Unsupported startup file encoding.");
        var encoding = unicode ? (Encoding)Encoding.Unicode : Encoding.UTF8;
        var suffix = "\r\n# Paw's Launcher: local RWD mods (last has priority)\r\n"
            + string.Join("\r\n", enabled.Select(m => "adddepot " + Mounted(m.Sha256))) + "\r\n";
        var startup = baseBytes.Concat(encoding.GetBytes(suffix)).ToArray();
        using var identity = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        identity.AppendData(startup);
        foreach (var mod in enabled)
        {
            ct.ThrowIfCancellationRequested();
            var source = Cache(mod.Sha256);
            RemovalSafety.CheckNoLinks(source);
            if (!File.Exists(source) || new FileInfo(source).Length != mod.Size
                || !string.Equals(await CryptoAndIO.Sha256Async(source, ct), mod.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The stored mod archive is missing or changed: " + mod.Name);
            using var archive = RwdArchive.Open(source);
            identity.AppendData(Convert.FromHexString(mod.Sha256));
        }
        var archiveHash = Convert.ToHexString(identity.GetHashAndReset());
        // Released native helpers accept version fields up to 48 characters.
        // Keep a 160-bit content identifier there and retain the complete digest.
        var version = archiveHash[..40].ToLowerInvariant();
        var payload = Path.Combine(game, ".pawpatch", "packages", ModuleId, version, "payload");
        RemovalSafety.CheckNoLinks(payload);
        Directory.CreateDirectory(payload);
        var module = new InstalledModule { Version = version, ArchiveSha256 = archiveHash, Enabled = true, Priority = int.MaxValue };
        foreach (var mod in enabled)
        {
            var relative = Mounted(mod.Sha256);
            var target = CryptoAndIO.SafeChildPath(payload, relative);
            RemovalSafety.CheckNoLinks(target);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await AtomicFileAsync(target, async output =>
            {
                using var input = File.OpenRead(Cache(mod.Sha256));
                await input.CopyToAsync(output, ct);
            });
            if (!string.Equals(await CryptoAndIO.Sha256Async(target, ct), mod.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Mod changed while preparing installation.");
            module.Files.Add(new ModuleFile { Path = relative, Sha256 = mod.Sha256, Size = mod.Size });
        }
        var startupTarget = CryptoAndIO.SafeChildPath(payload, Startup);
        RemovalSafety.CheckNoLinks(startupTarget);
        Directory.CreateDirectory(Path.GetDirectoryName(startupTarget)!);
        await AtomicFileAsync(startupTarget, output => output.WriteAsync(startup, ct).AsTask());
        module.Files.Add(new ModuleFile { Path = Startup, Sha256 = Convert.ToHexString(SHA256.HashData(startup)), Size = startup.Length });
        result.Add(ModuleId, module);
        return result;
    }

    static async Task AtomicFileAsync(string target, Func<FileStream, Task> write)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { await write(output); output.Flush(true); }
            RemovalSafety.CheckNoLinks(target);
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    async Task<byte[]> ReadBaseStartupAsync(Dictionary<string, InstalledModule> desired, InstallState previous, CancellationToken ct)
    {
        string? source = null, hash = null;
        foreach (var (id, module) in desired.Where(p => p.Value.Enabled).OrderBy(p => p.Value.Priority).ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (module.Remove.Any(p => CryptoAndIO.NormalizeRelativePath(p) == Startup))
                throw new InvalidDataException("The selected component removes the game startup file.");
            foreach (var file in module.Files.Where(f => CryptoAndIO.NormalizeRelativePath(f.Path).Equals(Startup, StringComparison.OrdinalIgnoreCase)))
            {
                source = CryptoAndIO.SafeChildPath(Path.Combine(game, ".pawpatch", "packages"), id + "/" + module.Version + "/payload/" + Startup);
                hash = file.Sha256;
            }
        }
        if (source is null)
        {
            if (previous.Originals.TryGetValue(Startup, out var original))
            {
                if (!original.Existed || original.BackupRelativePath is null) throw new InvalidDataException("No original game startup file is available.");
                source = CryptoAndIO.SafeChildPath(Path.Combine(game, ".pawpatch", "originals"), original.BackupRelativePath);
                hash = original.Sha256;
            }
            else source = Path.Combine(game, Startup);
        }
        RemovalSafety.CheckNoLinks(source);
        if (!File.Exists(source) || new FileInfo(source).Length > 1024 * 1024) throw new InvalidDataException("The game startup file is missing or too large.");
        var bytes = await File.ReadAllBytesAsync(source, ct);
        if (hash is not null && !Convert.ToHexString(SHA256.HashData(bytes)).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original game startup file has changed.");
        return bytes;
    }
}
