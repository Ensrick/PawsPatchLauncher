using System.Text.Json;

namespace PawsPatchLauncher;

public sealed class PatchTransaction
{
    public string Phase { get; set; } = "prepared";
    public bool IsRollback { get; set; }
    public InstallState Before { get; set; } = new();
    public List<SnapshotFile> Files { get; set; } = [];
}

public sealed class SnapshotFile
{
    public string Path { get; set; } = "";
    public bool Existed { get; set; }
    public string Sha256 { get; set; } = "";
}

public sealed class PatchRecovery(string gameRoot)
{
    private readonly string _game = Path.GetFullPath(gameRoot);
    private string Control => Path.Combine(_game, ".pawpatch");
    private string Pointer => Path.Combine(Control, "rollback.txt");
    public bool CanRollback => File.Exists(Pointer);

    public static string GamePath(string root, string relative)
    {
        var path = CryptoAndIO.SafeChildPath(root, relative);
        var reserved = Path.Combine(Path.GetFullPath(root), ".pawpatch") + Path.DirectorySeparatorChar;
        if (path.StartsWith(reserved, StringComparison.OrdinalIgnoreCase) || path.Equals(reserved.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("A package cannot change launcher recovery files.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("A managed game file is a symbolic link: " + path);
        // Do not traverse a junction into another folder while updating or restoring files.
        for (var parent = Path.GetDirectoryName(path); parent is not null && parent.Length >= Path.GetFullPath(root).Length; parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) && (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A junction is not allowed in managed game paths: " + parent);
        return path;
    }

    public async Task<(string Directory, PatchTransaction Journal)> CaptureAsync(IEnumerable<string> paths, InstallState state, bool rollback = false, CancellationToken ct = default)
    {
        var directory = Path.Combine(Control, "transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var journal = new PatchTransaction { Before = state, IsRollback = rollback };
        foreach (var relative in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var target = GamePath(_game, relative);
            var entry = new SnapshotFile { Path = relative, Existed = File.Exists(target) };
            if (entry.Existed)
            {
                var backup = CryptoAndIO.SafeChildPath(Path.Combine(directory, "files"), relative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup);
                entry.Sha256 = await CryptoAndIO.Sha256Async(backup, ct);
            }
            journal.Files.Add(entry);
        }
        await SaveAsync(directory, journal, ct);
        return (directory, journal);
    }

    public static Task SaveAsync(string directory, PatchTransaction journal, CancellationToken ct = default)
        => CryptoAndIO.AtomicWriteTextAsync(Path.Combine(directory, "journal.json"), JsonSerializer.Serialize(journal, LauncherJsonContext.Default.PatchTransaction), ct);

    public async Task CommitAsync(string directory, PatchTransaction journal)
    {
        journal.Phase = "committed";
        await SaveAsync(directory, journal);
        await PublishPointerAsync(directory, journal);
        PruneCompletedBackups();
    }

    private void PruneCompletedBackups()
    {
        var root = Path.Combine(Control, "transactions");
        var keep = File.Exists(Pointer) ? File.ReadAllText(Pointer).Trim() : null;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(directory);
            if (name == keep || !Guid.TryParseExact(name, "N", out _)) continue;
            var journalPath = Path.Combine(directory, "journal.json");
            try
            {
                if (!File.Exists(journalPath)) continue;
                var journal = JsonSerializer.Deserialize(File.ReadAllText(journalPath), LauncherJsonContext.Default.PatchTransaction);
                if (journal?.Phase is "complete" or "recovered")
                    Directory.Delete(CryptoAndIO.SafeChildPath(root, name), true);
            }
            catch (IOException) { } // Cleanup may be retried after another successful update.
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task PublishPointerAsync(string directory, PatchTransaction journal)
    {
        if (journal.IsRollback) { if (File.Exists(Pointer)) File.Delete(Pointer); }
        else await CryptoAndIO.AtomicWriteTextAsync(Pointer, Path.GetFileName(directory));
        journal.Phase = "complete";
        await SaveAsync(directory, journal);
    }

    public async Task RestoreAsync(string directory, PatchTransaction journal)
    {
        // Validate the entire backup before changing even one live file.
        foreach (var entry in journal.Files)
        {
            GamePath(_game, entry.Path);
            if (!entry.Existed) continue;
            var backup = CryptoAndIO.SafeChildPath(Path.Combine(directory, "files"), entry.Path);
            if (!File.Exists(backup) || !string.Equals(await CryptoAndIO.Sha256Async(backup), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Recovery backup is missing or damaged: " + entry.Path);
        }
        foreach (var entry in journal.Files)
        {
            var target = GamePath(_game, entry.Path);
            if (entry.Existed)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var temporary = target + ".pawpatch.tmp";
                File.Copy(CryptoAndIO.SafeChildPath(Path.Combine(directory, "files"), entry.Path), temporary, true);
                File.Move(temporary, target, true);
            }
            else if (File.Exists(target)) File.Delete(target);
        }
        await CryptoAndIO.AtomicWriteTextAsync(Path.Combine(Control, "state.json"), JsonSerializer.Serialize(journal.Before, LauncherJsonContext.Default.InstallState));
    }

    public Task<int> RecoverInterruptedAsync() => Task.Run(RecoverInterruptedCoreAsync);

    private async Task<int> RecoverInterruptedCoreAsync()
    {
        var root = Path.Combine(Control, "transactions");
        if (!Directory.Exists(root)) return 0;
        var recovered = 0;
        foreach (var directory in Directory.EnumerateDirectories(root).OrderBy(Directory.GetCreationTimeUtc))
        {
            var path = Path.Combine(directory, "journal.json");
            if (!File.Exists(path)) continue;
            var journal = JsonSerializer.Deserialize(File.ReadAllText(path), LauncherJsonContext.Default.PatchTransaction)
                ?? throw new InvalidDataException("Invalid recovery journal.");
            if (journal.Phase == "committed") { await PublishPointerAsync(directory, journal); continue; }
            if (journal.Phase != "prepared") continue;
            await RestoreAsync(directory, journal);
            journal.Phase = "recovered";
            await SaveAsync(directory, journal);
            recovered++;
        }
        return recovered;
    }

    public Task<InstallState> RollbackAsync(InstallState current) => Task.Run(() => RollbackCoreAsync(current));

    private async Task<InstallState> RollbackCoreAsync(InstallState current)
    {
        await RecoverInterruptedAsync();
        if (!CanRollback) throw new InvalidOperationException("No previous patch installation is available.");
        var directory = CryptoAndIO.SafeChildPath(Path.Combine(Control, "transactions"), File.ReadAllText(Pointer).Trim());
        var journal = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(directory, "journal.json")), LauncherJsonContext.Default.PatchTransaction)
            ?? throw new InvalidDataException("Invalid rollback journal.");
        var rescue = await CaptureAsync(journal.Files.Select(x => x.Path), current, rollback: true);
        try
        {
            await RestoreAsync(directory, journal);
            await CommitAsync(rescue.Directory, rescue.Journal);
            return journal.Before;
        }
        catch
        {
            if (rescue.Journal.Phase == "prepared")
            {
                await RestoreAsync(rescue.Directory, rescue.Journal);
                rescue.Journal.Phase = "recovered";
                await SaveAsync(rescue.Directory, rescue.Journal);
            }
            throw;
        }
    }
}
