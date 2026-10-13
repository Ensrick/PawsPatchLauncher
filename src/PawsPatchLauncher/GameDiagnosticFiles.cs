using Microsoft.Win32;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PawsPatchLauncher;

public sealed record DiagnosticLocations(string GameRoot, string Documents, string LocalAppData,
    string ProgramData, IReadOnlyList<string> CustomDumpFolders)
{
    public static DiagnosticLocations Current(string gameRoot)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Read existing Windows settings only. Do not enable dumps or change retention.
        // https://learn.microsoft.com/windows/win32/wer/collecting-user-mode-dumps
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        foreach (var suffix in new[] { "", @"\k2.exe" })
        {
            try
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps" + suffix);
                if (key?.GetValue("DumpFolder") is string path && !string.IsNullOrWhiteSpace(path))
                    folders.Add(Environment.ExpandEnvironmentVariables(path));
            }
            catch (Exception e) when (e is SecurityException or UnauthorizedAccessException or IOException) { }
        }
        return new(gameRoot, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), folders.ToArray());
    }

    public string? VirtualGameRoot()
    {
        var full = Path.GetFullPath(GameRoot);
        if (full.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        return Path.Combine(LocalAppData, "VirtualStore", full[Path.GetPathRoot(full)!.Length..]);
    }
}

public sealed class DiagnosticCollectionReport
{
    public int SchemaVersion { get; } = 1;
    public int RecentGroupsPerCategory { get; } = GameDiagnosticFiles.RecentCount;
    public List<DiagnosticSearchResult> Searches { get; } = [];
    public List<DiagnosticCopyResult> Files { get; } = [];
    public string Note { get; } = "Five newest readable game log sets, game dumps, Windows dumps and WER reports are collected separately, plus companion logs and current patch status. Files are never created retroactively. Missing sources and copy failures are listed. Backups, saves, chat text and other applications' Windows dumps are excluded.";
}
public sealed record DiagnosticSearchResult(string Source, string Path, string Status, string? Error = null);
public sealed record DiagnosticCopyResult(string Source, string ArchivePath, string Category, string Status,
    long Bytes, DateTime? ModifiedUtc, string? Error = null);

public static class GameDiagnosticFiles
{
    public const int RecentCount = 5;
    private static readonly StringComparer Paths = StringComparer.OrdinalIgnoreCase;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Regex LogName = new(@"^(?:(?:ART|SAI)_)?(?<session>log[^/\\]*)\.log$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GameExe = new(@"^k2(?:_paws_[a-z0-9_]+)?\.exe$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WindowsDump = new(@"^k2(?:_paws_[a-z0-9_]+)?\.exe(?:\.[0-9]+)?\.(?:dmp|mdmp|hdmp)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Excluded = new([".pawpatch", "PawsPatchBackups", "backups", "backup", "originals", "data orig", "save", "saves", "screenshots", "cache", "test-profile"], Paths);
    private sealed record Candidate(string Path, string ArchivePath, string Category, string Group, DateTime ModifiedUtc);
    private sealed record Group(string Key, string Category, DateTime ModifiedUtc, Candidate[] Files);

    public static async Task<DiagnosticCollectionReport> CollectAsync(string staging, DiagnosticLocations locations,
        CancellationToken token = default, DiagnosticCollectionReport? report = null)
    {
        report ??= new();
        var candidates = new Dictionary<string, Candidate>(Paths);
        var visited = new HashSet<string>(Paths);
        void Add(string path, string sourceRoot, string label, string category, string group)
        {
            try
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                var full = Path.GetFullPath(path);
                var relative = CryptoAndIO.NormalizeRelativePath(Path.GetRelativePath(sourceRoot, full));
                candidates.TryAdd(full, new(full, "logs/" + label + "/" + relative, category, group, File.GetLastWriteTimeUtc(full)));
            }
            catch (Exception e) when (FileError(e)) { report.Searches.Add(new(label, path, "unreadable", e.GetType().Name)); }
        }
        void Scan(string sourceRoot, string label, bool recursive, bool windowsOnly = false)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot)) return;
            if (sourceRoot.StartsWith(@"\\", StringComparison.Ordinal)) { report.Searches.Add(new(label, sourceRoot, "remote-path-skipped")); return; }
            var pending = new Stack<string>(); pending.Push(sourceRoot);
            while (pending.Count != 0)
            {
                token.ThrowIfCancellationRequested();
                var directory = pending.Pop();
                try
                {
                    if (!visited.Add(Path.GetFullPath(directory))) continue;
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    { report.Searches.Add(new(label, directory, "link-skipped")); continue; }
                    var files = Directory.GetFiles(directory);
                    report.Searches.Add(new(label, directory, "searched"));
                    foreach (var path in files)
                    {
                        var name = Path.GetFileName(path);
                        if (windowsOnly)
                        {
                            if (WindowsDump.IsMatch(name)) Add(path, sourceRoot, label, "windows-dumps", path);
                            continue;
                        }
                        var log = LogName.Match(name);
                        if (log.Success) Add(path, sourceRoot, label, "game-logs", Path.Combine(directory, log.Groups["session"].Value));
                        else if (Path.GetExtension(name).Equals(".dmp", StringComparison.OrdinalIgnoreCase))
                            Add(path, sourceRoot, label, "game-dumps", path);
                        else if (name.StartsWith("synclog", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                            Add(path, sourceRoot, label, "sync-logs", path);
                        else if (name.Equals("paws_graphics_guard.log", StringComparison.OrdinalIgnoreCase)
                            || name.Equals("paws_recovery.log", StringComparison.OrdinalIgnoreCase)
                            || name.StartsWith("paws_", StringComparison.OrdinalIgnoreCase) && name.EndsWith("status.txt", StringComparison.OrdinalIgnoreCase))
                            Add(path, sourceRoot, label, "status:" + name.ToLowerInvariant(), path);
                    }
                    if (recursive)
                        foreach (var child in Directory.GetDirectories(directory))
                            if (!Excluded.Contains(Path.GetFileName(child)) && !Path.GetFileName(child).StartsWith("Before-", StringComparison.OrdinalIgnoreCase)) pending.Push(child);
                }
                catch (DirectoryNotFoundException) { report.Searches.Add(new(label, directory, "missing")); }
                catch (FileNotFoundException) { report.Searches.Add(new(label, directory, "missing")); }
                catch (Exception e) when (FileError(e)) { report.Searches.Add(new(label, directory, "unreadable", e.GetType().Name)); }
            }
        }
        void ScanGameRoot(string root, string label)
        {
            Scan(root, label, false);
            foreach (var sub in new[] { "logs", "log", "crash", "crashes", "data/logs", "data/synclogs" })
                Scan(Path.Combine(root, sub), label + "/" + sub, true);
        }
        ScanGameRoot(locations.GameRoot, "game-root");
        Scan(Path.Combine(locations.Documents, "Kohan2"), "documents-kohan2", true);
        Scan(Path.Combine(locations.Documents, "Kohan II"), "documents-kohan-ii", true);
        if (locations.VirtualGameRoot() is { } virtualRoot) ScanGameRoot(virtualRoot, "virtualstore-game");
        Scan(Path.Combine(locations.LocalAppData, "CrashDumps"), "windows-crashdumps", false, true);
        for (var i = 0; i < locations.CustomDumpFolders.Count; i++) Scan(locations.CustomDumpFolders[i], "windows-custom-" + i, false, true);

        void ScanWer(string root, string label)
        {
            try
            {
                if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) { report.Searches.Add(new(label, root, "link-skipped")); return; }
                var folders = Directory.GetDirectories(root);
                report.Searches.Add(new(label, root, "searched"));
                foreach (var folder in folders)
                {
                    token.ThrowIfCancellationRequested();
                    // Do not inspect other applications' report bodies.
                    if (!Regex.IsMatch(Path.GetFileName(folder), @"^(?:AppCrash|AppHang)_k2(?:\.exe_|_paws_)", RegexOptions.IgnoreCase)) continue;
                    try
                    {
                        if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0) continue;
                        var wer = Path.Combine(folder, "Report.wer");
                        if (!File.Exists(wer) || new FileInfo(wer).Length > 256 * 1024) { report.Searches.Add(new(label, folder, "unverified-game-report")); continue; }
                        if ((File.GetAttributes(wer) & FileAttributes.ReparsePoint) != 0) continue;
                        var body = File.ReadAllText(wer);
                        var identity = body.Split('\n').Select(s => s.Trim()).Any(s =>
                            (s.StartsWith("AppName=", StringComparison.OrdinalIgnoreCase) || s.StartsWith("AppPath=", StringComparison.OrdinalIgnoreCase) || s.StartsWith("NsAppName=", StringComparison.OrdinalIgnoreCase))
                            && GameExe.IsMatch(Path.GetFileName(s[(s.IndexOf('=') + 1)..])));
                        if (!identity) { report.Searches.Add(new(label, folder, "unrelated-report-skipped")); continue; }
                        foreach (var path in Directory.GetFiles(folder))
                            if (Path.GetFileName(path).Equals("Report.wer", StringComparison.OrdinalIgnoreCase)
                                || new[] { ".dmp", ".mdmp", ".hdmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                                Add(path, root, label, "windows-reports", folder);
                    }
                    catch (Exception e) when (FileError(e)) { report.Searches.Add(new(label, folder, "unreadable", e.GetType().Name)); }
                }
            }
            catch (DirectoryNotFoundException) { report.Searches.Add(new(label, root, "missing")); }
            catch (FileNotFoundException) { report.Searches.Add(new(label, root, "missing")); }
            catch (Exception e) when (FileError(e)) { report.Searches.Add(new(label, root, "unreadable", e.GetType().Name)); }
        }
        foreach (var (directory, label) in new[] { (locations.LocalAppData, "user-wer"), (locations.ProgramData, "system-wer") })
        foreach (var area in new[] { "ReportArchive", "ReportQueue" })
            ScanWer(Path.Combine(directory, "Microsoft/Windows/WER", area), label + "/" + area);

        var copied = new HashSet<string>(Paths);
        var groups = candidates.Values.GroupBy(c => c.Category + "\0" + c.Group, Paths)
            .Select(g => new Group(g.First().Group, g.First().Category, g.Max(c => c.ModifiedUtc), g.OrderBy(c => c.Path, Paths).ToArray())).ToArray();
        async Task<bool> Copy(Candidate file)
        {
            if (copied.Contains(file.Path)) return true;
            var success = await CopyFileAsync(file.Path, staging, file.ArchivePath, file.Category, report, token);
            if (success) copied.Add(file.Path);
            return success;
        }
        foreach (var bucket in groups.GroupBy(g => g.Category))
        {
            var successes = 0;
            var limit = bucket.Key.StartsWith("status:", StringComparison.Ordinal) ? 1 : RecentCount;
            foreach (var group in bucket.OrderByDescending(g => g.ModifiedUtc).ThenBy(g => g.Key, Paths))
            {
                if (successes >= limit) break;
                var complete = true;
                foreach (var file in group.Files) complete &= await Copy(file);
                if (complete) successes++;
            }
        }
        // An older crash dump can be retained even when five newer ordinary logs exist.
        // Keep its main/ART/SAI logs too, without counting them against those five.
        foreach (var dump in candidates.Values.Where(c => c.Category == "game-dumps" && copied.Contains(c.Path)))
        {
            var key = Path.Combine(Path.GetDirectoryName(dump.Path)!, Path.GetFileNameWithoutExtension(dump.Path));
            foreach (var companion in candidates.Values.Where(c => c.Category == "game-logs" && Paths.Equals(c.Group, key))) await Copy(companion);
        }
        await WriteReportAsync(staging, report, token);
        return report;
    }

    public static async Task<bool> CopyFileAsync(string source, string staging, string relative, string category,
        DiagnosticCollectionReport report, CancellationToken token)
    {
        var target = CryptoAndIO.SafeChildPath(staging, relative);
        long bytes = 0; DateTime? modified = null; var created = false;
        try
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("LinkSkipped");
            modified = File.GetLastWriteTimeUtc(source);
            // Snapshot the current length: don't chase a growing log or alter the original.
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var remaining = input.Length;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                created = true;
                var buffer = new byte[65536];
                while (remaining > 0)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), token);
                    if (read == 0) throw new EndOfStreamException("SourceTruncated");
                    await output.WriteAsync(buffer.AsMemory(0, read), token); bytes += read; remaining -= read;
                }
            }
            File.SetLastWriteTimeUtc(target, modified.Value);
            report.Files.Add(new(source, relative.Replace('\\', '/'), category, "copied", bytes, modified));
            return true;
        }
        catch (Exception e) when (FileError(e) || e is OperationCanceledException)
        {
            // Only remove our partial staging file, never the source.
            try { if (created && File.Exists(target)) File.Delete(target); } catch (Exception cleanup) when (FileError(cleanup)) { }
            if (e is OperationCanceledException) throw;
            report.Files.Add(new(source, relative.Replace('\\', '/'), category, "failed", bytes, modified, e.GetType().Name));
            return false;
        }
    }
    private static bool FileError(Exception e) => e is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
    public static Task WriteReportAsync(string staging, DiagnosticCollectionReport report, CancellationToken token) =>
        File.WriteAllTextAsync(Path.Combine(staging, "collection-report.json"), JsonSerializer.Serialize(report, JsonOptions), new UTF8Encoding(false), token);
}
