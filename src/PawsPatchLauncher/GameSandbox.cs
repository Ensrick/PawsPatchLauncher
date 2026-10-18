using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace PawsPatchLauncher;

/// <summary>
/// Single-player sandbox for the Kohan II 1.3.68 Steam executable (source: game/sandbox): computer players stay idle,
/// clicking any unit takes control of its kingdom, every player kingdom's sight is shared, two kingdoms' economies stay
/// in a fixed order on the native resource bar and the row under it, and the minimap colours objects by kingdom. The
/// game folder is never changed. The launcher keeps its own copy of it, adds the sandbox's stand-in <c>shfolder.dll</c> to that copy
/// only, and starts the copy, so the game folder, its file check and multiplayer stay as they are.
/// </summary>
public sealed class GameSandbox
{
    /// <summary>The Steam public-branch k2.exe of Kohan II 1.3.68 (build 24805354), the only executable the sandbox patches.</summary>
    public const string SupportedK2ExeSha256 = "CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A";
    public const string SteamAppId = "97130";
    public const string DllName = "shfolder.dll";
    const string Resource = "PawsPatchLauncher.Assets.Sandbox.shfolder.dll";
    const string ManifestName = ".k2sandbox-files.json";
    // Launcher state and the game's own logs stay out of the copy; the copy writes its own logs.
    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase) { ".pawpatch", "Logs" };

    readonly string _game, _copy, _exeSha256;

    public GameSandbox(string gameRoot, string copyRoot, string exeSha256 = SupportedK2ExeSha256)
    {
        _game = Path.GetFullPath(gameRoot);
        _copy = Path.GetFullPath(copyRoot);
        _exeSha256 = exeSha256;
    }

    public string CopyRoot => _copy;
    public string Executable => Path.Combine(_copy, "k2.exe");

    /// <summary>Next to the launcher, or inside a disposable test profile.</summary>
    public static string DefaultCopyRoot => ActivityStore.LocalTestProfile is { Length: > 0 } || ActivityStore.IsSmokeTest
        ? Path.Combine(ActivityStore.Root, "Sandbox game")
        : Path.Combine(AppContext.BaseDirectory, "Sandbox game");

    public static byte[] Dll()
    {
        using var stream = typeof(GameSandbox).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidDataException("The sandbox module is missing from this launcher build.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    sealed record Entry(long Size, long Ticks);

    /// <summary>
    /// Brings the copy up to date with the game folder: files whose size or time changed since the last copy are
    /// copied again, files this copy placed that the game folder no longer has are removed, and the sandbox module is
    /// written. Files the copied game writes itself (logs, sandbox.log) are never touched. Returns the files copied.
    /// </summary>
    public async Task<int> SyncAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (Inside(_copy, _game) || Inside(_game, _copy))
            throw new InvalidOperationException("The sandbox copy must be outside the game folder: " + _copy);
        var gameExe = Path.Combine(_game, "k2.exe");
        if (!File.Exists(gameExe)) throw new FileNotFoundException("k2.exe was not found in the game folder.", gameExe);
        if (!(await CryptoAndIO.Sha256Async(gameExe, ct)).Equals(_exeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The sandbox needs the Kohan II 1.3.68 executable (Steam public branch).");
        RemovalSafety.CheckNoLinks(_copy);
        Directory.CreateDirectory(_copy);

        var manifestPath = Path.Combine(_copy, ManifestName);
        var manifest = LoadManifest(manifestPath);
        var source = SourceFiles().ToList();
        var pending = source.Where(file =>
        {
            var info = new FileInfo(Path.Combine(_game, file));
            var target = Path.Combine(_copy, file);
            return !(manifest.TryGetValue(file, out var entry) && entry.Size == info.Length && entry.Ticks == info.LastWriteTimeUtc.Ticks
                && File.Exists(target) && new FileInfo(target).Length == info.Length);
        }).ToList();
        long total = pending.Sum(file => new FileInfo(Path.Combine(_game, file)).Length), done = 0;
        foreach (var file in pending)
        {
            ct.ThrowIfCancellationRequested();
            var from = Path.Combine(_game, file);
            var info = new FileInfo(from);
            var target = CryptoAndIO.SafeChildPath(_copy, file);
            RemovalSafety.CheckNoLinks(Path.GetDirectoryName(target)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await CopyAtomicAsync(from, target, ct);
            manifest[file] = new Entry(info.Length, info.LastWriteTimeUtc.Ticks);
            done += info.Length;
            progress?.Report(total == 0 ? 100 : done * 100.0 / total);
        }
        var present = source.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in manifest.Keys.Where(file => !present.Contains(file)).ToList())
        {
            var target = CryptoAndIO.SafeChildPath(_copy, stale);
            RemovalSafety.CheckNoLinks(target);
            if (File.Exists(target)) File.Delete(target);
            manifest.Remove(stale);
        }
        await CryptoAndIO.AtomicWriteTextAsync(manifestPath, JsonSerializer.Serialize(
            manifest.ToDictionary(p => p.Key, p => new[] { p.Value.Size, p.Value.Ticks })));

        var dll = Dll();
        var dllPath = Path.Combine(_copy, DllName);
        RemovalSafety.CheckNoLinks(dllPath);
        if (!File.Exists(dllPath) || !SHA256.HashData(await File.ReadAllBytesAsync(dllPath, ct)).AsSpan().SequenceEqual(SHA256.HashData(dll)))
        {
            var temporary = dllPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { await File.WriteAllBytesAsync(temporary, dll, ct); File.Move(temporary, dllPath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        // The copy's executable is the one that runs; check it as well as the original.
        if (!(await CryptoAndIO.Sha256Async(Executable, ct)).Equals(_exeSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The sandbox copy of k2.exe does not match the game's executable.");
        return pending.Count;
    }

    /// <summary>Starts the copy with the Steam app id in its environment, as Steam would, since it is outside the Steam library.</summary>
    public ProcessStartInfo StartInfo()
    {
        var start = new ProcessStartInfo(Executable) { WorkingDirectory = _copy, UseShellExecute = false };
        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("K2SB_", StringComparison.OrdinalIgnoreCase)).ToList())
            start.Environment.Remove(key);
        start.Environment["SteamAppId"] = SteamAppId;
        start.Environment["SteamGameId"] = SteamAppId;
        return start;
    }

    IEnumerable<string> SourceFiles()
    {
        // Same walk as the multiplayer check: no junctions or symbolic links are followed.
        var pending = new Stack<string>();
        pending.Push(_game);
        while (pending.TryPop(out var folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) continue;
                var relative = CryptoAndIO.NormalizeRelativePath(Path.GetRelativePath(_game, file));
                if (relative.Equals(DllName, StringComparison.OrdinalIgnoreCase) || relative.Equals(ManifestName, StringComparison.OrdinalIgnoreCase)) continue;
                yield return relative;
            }
            foreach (var directory in Directory.EnumerateDirectories(folder))
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0
                    && !(folder == _game && SkippedFolders.Contains(Path.GetFileName(directory)))) pending.Push(directory);
        }
    }

    static Dictionary<string, Entry> LoadManifest(string path)
    {
        var result = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, long[]>>(File.ReadAllText(path)) ?? [];
            foreach (var (file, values) in saved)
                if (values is { Length: 2 }) result[CryptoAndIO.NormalizeRelativePath(file)] = new Entry(values[0], values[1]);
        }
        catch (JsonException) { } // A damaged list only means every file is copied again.
        return result;
    }

    static async Task CopyAtomicAsync(string from, string target, CancellationToken ct)
    {
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var input = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await input.CopyToAsync(output, ct);
            File.SetLastWriteTimeUtc(temporary, File.GetLastWriteTimeUtc(from));
            File.Move(temporary, target, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    static bool Inside(string path, string folder)
    {
        var root = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.Equals(folder, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}
