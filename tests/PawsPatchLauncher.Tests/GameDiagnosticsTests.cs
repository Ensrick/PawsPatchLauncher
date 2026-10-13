using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PawsPatchLauncher;

internal static class GameDiagnosticsTests
{
    internal static async Task<int> RunAsync(string root, bool completeArchive = false)
    {
        var fixture = Path.Combine(Path.GetFullPath(root), "crash-history-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var locations = new DiagnosticLocations(Path.Combine(fixture, "game"), Path.Combine(fixture, "documents"),
            Path.Combine(fixture, "local"), Path.Combine(fixture, "program-data"), [Path.Combine(fixture, "custom-dumps")]);
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var count = 0;
        void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); count++; }
        string Add(string directory, string name, int age, string? text = null)
        {
            var path = Path.GetFullPath(Path.Combine(directory, name)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var data = Encoding.UTF8.GetBytes(text ?? "synthetic " + name);
            File.WriteAllBytes(path, data); File.SetLastWriteTimeUtc(path, epoch.AddMinutes(age)); expected[path] = data; return path;
        }
        for (var i = 1; i <= 13; i++)
        {
            foreach (var prefix in new[] { "", "ART_", "SAI_" }) Add(locations.GameRoot, prefix + "log-" + i + ".log", i);
            if (i <= 7) Add(locations.GameRoot, "log-" + i + ".dmp", i);
        }
        var lockedPath = Add(locations.GameRoot, "log-99.dmp", 99);
        Add(locations.VirtualGameRoot()!, "log-7.dmp", 14, "virtual store dump");
        Add(locations.VirtualGameRoot()!, "log-7.log", 14, "virtual store log");
        Add(Path.Combine(locations.Documents, "Kohan2"), "log-7.dmp", 15, "documents dump");
        Add(Path.Combine(locations.Documents, "Kohan2"), "log-7.log", 15, "documents log");
        Add(locations.GameRoot, "paws_graphics_guard.log", 25);
        Add(locations.GameRoot, "Logs/paws_recovery.log", 25);
        Add(locations.GameRoot, "paws_sync_family_herd_relations_1372_status.txt", 25);
        var syncFolder = Path.Combine(locations.Documents, "Kohan2", "data", "synclogs");
        for (var i = 1; i <= 6; i++) Add(syncFolder, $"synclog_film_{i}.txt", i, "Old replay history");
        var firstDesync = Add(syncFolder, "synclog_7.txt", 30,
            "Host/Client: Client\nIndex: 304230\nControl Word: 0x9001F\nName Type Value Checksum\nfirst mismatch evidence\n");
        Add(locations.GameRoot, "d3d9.dll", 25, "synthetic DLL, never executed");
        Add(locations.GameRoot, "k2.exe", 25, "synthetic EXE, never executed");
        Add(locations.GameRoot, "save-sentinel.rsg", 100, "DO NOT CHANGE");
        Add(Path.Combine(locations.Documents, "Kohan2/PawsPatchBackups"), "log-1000.dmp", 1000, "OLD BACKUP DO NOT COLLECT");
        Add(Path.Combine(locations.Documents, "Kohan2/data orig"), "log-1001.log", 1001, "OLD BACKUP");
        Add(Path.Combine(locations.Documents, "Kohan2"), "passwords.txt", 2000, "NOT DIAGNOSTICS");
        var windows = Path.Combine(locations.LocalAppData, "CrashDumps");
        for (var i = 1; i <= 7; i++) Add(windows, "k2.exe." + i + ".dmp", i);
        Add(windows, "chrome.exe.1.dmp", 2000, "OTHER APP");
        Add(windows, "notk2.exe.1.dmp", 2000, "OTHER APP");
        Add(locations.CustomDumpFolders[0], "k2.exe.20.dmp", 20);
        Add(locations.CustomDumpFolders[0], "other.exe.21.dmp", 21, "OTHER APP");
        for (var i = 1; i <= 6; i++)
        {
            var folder = Path.Combine(locations.ProgramData, "Microsoft/Windows/WER/ReportArchive/AppCrash_k2.exe_" + i);
            Add(folder, "Report.wer", i, "Version=1\nAppName=k2.exe\nAppPath=C:\\Game\\k2.exe\n");
            Add(folder, "minidump.mdmp", i);
        }
        var spoof = Path.Combine(locations.LocalAppData, "Microsoft/Windows/WER/ReportQueue/AppCrash_k2.exe_spoof");
        Add(spoof, "Report.wer", 1000, "AppName=another.exe\n"); Add(spoof, "memory.hdmp", 1000);
        var staging = Path.Combine(fixture, "staging"); Directory.CreateDirectory(staging);
        DiagnosticCollectionReport report;
        using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            report = await GameDiagnosticFiles.CollectAsync(staging, locations);
            Check(report.Files.Any(f => f.Source == lockedPath && f.Status == "failed"), "Locked latest dump was silently skipped");
            var copied = report.Files.Where(f => f.Status == "copied").ToArray();
            Check(copied.Count(f => f.Category == "game-dumps") == 5, "Need five readable game dumps despite locked newest file");
            Check(copied.Count(f => f.Category == "windows-dumps") == 5, "Windows quota must be independent from game dumps");
            Check(copied.Count(f => f.Category == "windows-reports" && f.ArchivePath.EndsWith("Report.wer")) == 5, "Need five game WER reports");
            Check(copied.Count(f => f.Category == "windows-reports" && f.ArchivePath.EndsWith(".mdmp")) == 5, "WER dump companion missing");
            Check(copied.Any(f => f.ArchivePath.Contains("virtualstore-game") && f.Source.EndsWith("log-7.dmp")), "VirtualStore dump missing");
            Check(copied.Any(f => f.ArchivePath.Contains("documents-kohan2") && f.Source.EndsWith("log-7.dmp")), "Documents dump missing");
            Check(copied.Any(f => f.Source == Path.Combine(locations.GameRoot, "log-5.dmp")), "Older accessible dump not used as fallback");
            Check(copied.Any(f => f.Source == Path.Combine(locations.GameRoot, "ART_log-5.log")), "Companion log lost below newest log cutoff");
            Check(copied.Any(f => f.Source == Path.Combine(locations.GameRoot, "SAI_log-5.log")), "SAI companion missing");
            Check(copied.All(f => !f.Source.EndsWith("log-1.dmp")), "Old dump should be outside retention");
            Check(copied.Any(f => f.Source.EndsWith("paws_graphics_guard.log")), "Graphics guard status missing");
            Check(copied.Any(f => f.Source.EndsWith("paws_recovery.log")), "Match recovery log missing");
            Check(copied.Any(f => f.Source == firstDesync), "First network desync capture missing from diagnostics");
            Check(copied.Count(f => f.Category == "sync-logs") == 5, "Native sync log retention changed");
            Check(copied.Any(f => f.Source.Contains("windows-custom-")) || copied.Any(f => f.ArchivePath.Contains("windows-custom-")), "Custom Windows folder ignored");
            Check(copied.All(f => !f.Source.Contains("chrome.exe") && !f.Source.Contains("notk2.exe") && !f.Source.Contains("other.exe") && !f.Source.Contains("spoof")), "Unrelated application dump collected");
            Check(copied.All(f => !f.Source.Contains("PawsPatchBackups") && !f.Source.Contains("data orig") && !f.Source.EndsWith("passwords.txt") && !f.Source.EndsWith(".rsg")), "Private/unrelated or backup file collected");
            Check(copied.Select(f => f.ArchivePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == copied.Length, "Duplicate archive names");
            foreach (var f in copied)
            {
                Check(File.ReadAllBytes(Path.Combine(staging, f.ArchivePath)).SequenceEqual(expected[f.Source]), "Copied bytes differ");
                Check(File.GetLastWriteTimeUtc(Path.Combine(staging, f.ArchivePath)) == f.ModifiedUtc, "Timestamp was lost");
            }
            if (completeArchive)
            {
                Check(ActivityStore.LocalTestProfile is not null || ActivityStore.IsSmokeTest, "Archive integration requires isolated launcher profile");
                var archivePath = Path.Combine(fixture, "diagnostics.zip");
                await DiagnosticsCollector.CreateAsync(archivePath, new(locations.GameRoot, Path.Combine(locations.GameRoot, "k2.exe"), null, "beta"), new UserSettings(), new InstallState(), [], locations: locations);
                using (var zip = ZipFile.OpenRead(archivePath))
                {
                    var archiveNames = zip.Entries.Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach (var f in copied) Check(archiveNames.Contains(f.ArchivePath), "Collected file missing from final ZIP");
                    Check(archiveNames.Contains("collection-report.json"), "Missing collection report");
                    using var inventory = new StreamReader(zip.GetEntry("runtime-files.txt")!.Open());
                    Check(inventory.ReadToEnd().Contains("d3d9.dll"), "Graphics proxy missing from runtime inventory");
                    using var manifest = new StreamReader(zip.GetEntry("files-sha256.txt")!.Open());
                    var rows = manifest.ReadToEnd().Split('\n').Where(l => l.Length > 65 && char.IsAsciiHexDigit(l[0])).ToArray();
                    foreach (var row in rows)
                    {
                        var fields = row.Trim().Split("  ", 3, StringSplitOptions.None);
                        var entry = zip.GetEntry(fields[2].Replace('\\', '/'))!;
                        using var stream = entry.Open();
                        Check(entry.Length == long.Parse(fields[1]) && Convert.ToHexString(SHA256.HashData(stream)) == fields[0], "Final ZIP digest mismatch");
                    }
                    Check(rows.Length == zip.Entries.Count - 1, "ZIP contains unmanifested files");
                }
                var oldZip = await File.ReadAllBytesAsync(archivePath);
                using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
                try { await DiagnosticsCollector.CreateAsync(archivePath, new(locations.GameRoot, "", null, null), new(), new(), [], cancellation.Token, locations); throw new Exception("Cancellation ignored"); }
                catch (OperationCanceledException) { Check((await File.ReadAllBytesAsync(archivePath)).SequenceEqual(oldZip), "Cancelled collection destroyed previous archive"); }
            }
        }
        foreach (var file in expected) Check(File.ReadAllBytes(file.Key).SequenceEqual(file.Value), "Source file was modified");
        var emptyStaging = Path.Combine(fixture, "empty-staging"); Directory.CreateDirectory(emptyStaging);
        var empty = await GameDiagnosticFiles.CollectAsync(emptyStaging, new(Path.Combine(fixture, "absent-game"), Path.Combine(fixture, "absent-docs"), Path.Combine(fixture, "absent-local"), Path.Combine(fixture, "absent-system"), []));
        Check(empty.Files.Count == 0 && empty.Searches.Any(s => s.Status == "missing"), "Missing files not reported");
        var existing = Path.Combine(emptyStaging, "existing.log"); await File.WriteAllTextAsync(existing, "keep previous");
        Check(!await GameDiagnosticFiles.CopyFileAsync(Path.Combine(locations.GameRoot, "log-5.log"), emptyStaging, "existing.log", "test", empty, default), "Staging overwrite should fail");
        Check(await File.ReadAllTextAsync(existing) == "keep previous", "Copy failure deleted an existing staging file");
        var embeddedRoot = Path.Combine(fixture, "embedded-recorder-game");
        var embeddedLogs = Path.Combine(embeddedRoot, "Logs", "PawsGraphics");
        var faultLog = Add(embeddedLogs, "log-graphics-20260920-124344-506-22760.log", 1, "CAPTURE first_chance=1\nDUMP result=1");
        var faultDump = Add(embeddedLogs, "log-graphics-20260920-124344-506-22760.dmp", 1, "synthetic dump");
        for (var i = 2; i <= 9; i++) Add(embeddedLogs, $"log-graphics-normal-{i}.log", i);
        var embeddedStage = Path.Combine(fixture, "embedded-recorder-staging"); Directory.CreateDirectory(embeddedStage);
        var embedded = await GameDiagnosticFiles.CollectAsync(embeddedStage, new(embeddedRoot,
            Path.Combine(fixture, "none-docs"), Path.Combine(fixture, "none-local"), Path.Combine(fixture, "none-system"), []));
        Check(embedded.Files.Any(f => f.Source.Equals(faultDump, StringComparison.OrdinalIgnoreCase) && f.Status == "copied"), "Embedded recorder dump missing");
        Check(embedded.Files.Any(f => f.Source.Equals(faultLog, StringComparison.OrdinalIgnoreCase) && f.Status == "copied"), "Embedded crash log lost after later normal sessions");
        Check(embedded.Files.Count(f => f.Category == "game-logs" && f.Status == "copied") == 6, "Expected five recent logs plus crash companion");
        foreach (var f in embedded.Files.Where(f => f.Status == "copied"))
            Check(File.ReadAllBytes(Path.Combine(embeddedStage, f.ArchivePath)).SequenceEqual(expected[f.Source]), "Embedded recorder file bytes differ");
        Console.WriteLine($"GAME DIAGNOSTICS PASS {count}: five recent readable groups, companion logs, embedded graphics recorder, VirtualStore, Windows/custom/WER filters, locked fallback, missing sources, original preservation, archive={completeArchive}");
        return count;
    }
}
