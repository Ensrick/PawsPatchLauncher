using PawsPatchLauncher;
using System.Net;
using System.Net.Http;
using System.Security.Authentication;
using System.Text.Json;

public static class EnhancementTests
{
    public static async Task<int> RunAsync(string root)
    {
        var count = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); count++; }
        void Reject(Action action) { var rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or FormatException or IOException) { rejected = true; } Check(rejected, "Unsafe input accepted"); }
        string H(char c) => new(c, 64);
        var game = Path.Combine(root, "enhancements", "game"); Directory.CreateDirectory(game);
        await File.WriteAllTextAsync(Path.Combine(game, "k2.exe"), "stock");
        await File.WriteAllTextAsync(Path.Combine(game, "helper.exe"), "helper");
        await File.WriteAllTextAsync(Path.Combine(game, "gameplay.tgi"), "data");
        var state = new InstallState { Modules = new() { ["core"] = new InstalledModule { Enabled = true, Version = "1", ArchiveSha256 = H('A'),
            Files = [new ModuleFile { Path = "helper.exe", Sha256 = await CryptoAndIO.Sha256Async(Path.Combine(game, "helper.exe")) },
                new ModuleFile { Path = "gameplay.tgi", Sha256 = await CryptoAndIO.Sha256Async(Path.Combine(game, "gameplay.tgi")) }] } } };
        var report = await MultiplayerCheck.CreateAsync(game, state, new UserSettings { ImprovedAi = false }, Path.Combine(game, "helper.exe"), "25068126");
        Check(report.Details is not null && report.Errors.Count == 0, "Detailed report missing");
        var local = report.Details!; MultiplayerDetails.Validate(local);
        Check(local.Fingerprint == report.Fingerprint && MultiplayerDetails.Fingerprint(local) == report.Fingerprint, "Legacy fingerprint changed");
        MultiplayerManifest Clone() => JsonSerializer.Deserialize(JsonSerializer.Serialize(local, LauncherJsonContext.Default.MultiplayerManifest), LauncherJsonContext.Default.MultiplayerManifest)!;
        var peer = Clone();
        Check(MultiplayerDetails.Compare(local, peer).Matches, "Identical reports differ");
        peer.Modules[0].Sha256 = peer.Modules[0].Sha256.ToLowerInvariant();
        Check(MultiplayerDetails.Compare(local, peer).Matches, "Hash case changed comparison");
        peer = Clone(); peer.Files.Single(x => x.Path == "gameplay.tgi").Sha256 = H('B'); peer.Fingerprint = MultiplayerDetails.Fingerprint(peer);
        var comparison = MultiplayerDetails.Compare(local, peer);
        Check(!comparison.Matches && comparison.Differences.Count == 1 && comparison.Differences[0].Name == "gameplay.tgi", "Changed file not identified");
        peer.Files.Single(x => x.Path == "gameplay.tgi").Sha256 = "MISSING"; peer.Fingerprint = MultiplayerDetails.Fingerprint(peer);
        Check(MultiplayerDetails.Compare(local, peer).Differences.Single().Peer == "MISSING", "Missing file not identified");
        peer = Clone(); peer.Files.Add(new() { Path = "extra.tgi", Sha256 = H('F') }); peer.Fingerprint = MultiplayerDetails.Fingerprint(peer);
        Check(MultiplayerDetails.Compare(local, peer).Differences.Single().Local == "NOT_LISTED", "Extra file not identified");
        peer = Clone(); peer.Modules[0].Version = "2"; peer.Configuration = ConfigurationCode.Create(new UserSettings { Channel = "beta", SiegeBalance = false, ImprovedAi = false }); peer.Fingerprint = MultiplayerDetails.Fingerprint(peer);
        Check(MultiplayerDetails.Compare(local, peer).Differences.Count == 3, "Component/settings differences missing");
        peer = Clone(); peer.IntegrityErrors.Add("gameplay.tgi");
        Check(!MultiplayerDetails.Compare(local, peer).Matches, "Damaged identical install reported green");
        foreach (var invalid in new[] { "..\\private.txt", "C:\\private.txt", "\\\\server\\private", "bad\nname", "folder/uncanonical.tgi" })
        {
            peer = Clone(); peer.Files.Add(new() { Path = invalid, Sha256 = H('C') }); peer.Fingerprint = MultiplayerDetails.Fingerprint(peer); Reject(() => MultiplayerDetails.Validate(peer));
        }
        peer = Clone(); peer.Files.Add(new() { Path = "K2.EXE", Sha256 = H('A') }); peer.Fingerprint = MultiplayerDetails.Fingerprint(peer); Reject(() => MultiplayerDetails.Validate(peer));
        peer = Clone(); peer.Fingerprint = "PAW-MP1-" + H('0'); Reject(() => MultiplayerDetails.Validate(peer));
        peer = Clone(); peer.SchemaVersion = 9; Reject(() => MultiplayerDetails.Validate(peer));
        peer = Clone(); peer.Files = null!; Reject(() => MultiplayerDetails.Validate(peer));
        peer = Clone(); peer.Files[0].Sha256 = "abc"; Reject(() => MultiplayerDetails.Validate(peer));
        var reportPath = Path.Combine(root, "friend.pawmp.json");
        await MultiplayerDetails.SaveAsync(reportPath, local);
        Check(MultiplayerDetails.Compare(local, await MultiplayerDetails.LoadAsync(reportPath)).Matches, "Report roundtrip failed");
        var json = await File.ReadAllTextAsync(reportPath);
        Check(!json.Contains(game, StringComparison.OrdinalIgnoreCase) && !json.Contains(Environment.UserName + "\\", StringComparison.OrdinalIgnoreCase), "Report exposed local paths");
        var oversized = Path.Combine(root, "too-large.pawmp.json");
        using (var file = File.Create(oversized)) file.SetLength(MultiplayerDetails.MaxBytes + 1L);
        var oversizeRejected = false; try { await MultiplayerDetails.LoadAsync(oversized); } catch (InvalidDataException) { oversizeRejected = true; }
        Check(oversizeRejected, "Oversized peer report accepted");

        // The Documents\Kohan2\data depot outranks the game folder and is outside the game's join check: its gameplay
        // files are reported under a prefix, the player's own folders are not, and an empty depot keeps the legacy code.
        var userData = Path.Combine(root, "enhancements", "documents", "Kohan2", "data");
        Directory.CreateDirectory(userData);
        Check((await MultiplayerCheck.CreateAsync(game, state, new UserSettings { ImprovedAi = false }, Path.Combine(game, "helper.exe"), "25068126", userData)).Fingerprint == report.Fingerprint,
            "An empty user data folder changed the fingerprint");
        foreach (var (relative, text) in new[] { ("Units\\Test\\loose.tgi", "loose"), ("User\\UVars.tgi", "settings"), ("Maps\\Mine\\map.tgi", "map"),
            ("Save\\game.rsg", "save"), ("readme.txt", "notes"), (".hidden\\x.tgi", "hidden") })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(userData, relative))!);
            await File.WriteAllTextAsync(Path.Combine(userData, relative), text);
        }
        var shadow = Path.Combine(game, "%USERDATA%", "data", "Units", "Test"); Directory.CreateDirectory(shadow);
        await File.WriteAllTextAsync(Path.Combine(shadow, "loose.tgi"), "game folder copy");
        var withUser = (await MultiplayerCheck.CreateAsync(game, state, new UserSettings { ImprovedAi = false }, Path.Combine(game, "helper.exe"), "25068126", userData)).Details!;
        var userEntries = withUser.Files.Where(x => x.Path.StartsWith(MultiplayerCheck.UserDataPrefix, StringComparison.Ordinal)).ToList();
        Check(userEntries.Count == 1 && userEntries[0].Path == MultiplayerCheck.UserDataPrefix + "Units\\Test\\loose.tgi"
            && userEntries[0].Sha256 == await CryptoAndIO.Sha256Async(Path.Combine(userData, "Units", "Test", "loose.tgi")), "User data gameplay file not reported exactly once with its own hash");
        Check(withUser.Files.Count == local.Files.Count + 1, "The player's own files or a game-folder %USERDATA% directory were reported");
        MultiplayerDetails.Validate(withUser);
        var userComparison = MultiplayerDetails.Compare(withUser, local);
        Check(!userComparison.Matches && userComparison.Differences.Single().Name == userEntries[0].Path && userComparison.Differences[0].Peer == "NOT_LISTED",
            "A loose user data file was not identified against a clean install");
        var userReportPath = Path.Combine(root, "friend-user-data.pawmp.json");
        await MultiplayerDetails.SaveAsync(userReportPath, withUser);
        Check(!(await File.ReadAllTextAsync(userReportPath)).Contains(root.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase), "Report exposed the user data path");
        Check(MultiplayerDetails.Compare(withUser, await MultiplayerDetails.LoadAsync(userReportPath)).Matches, "User data report roundtrip failed");
        File.Delete(Path.Combine(shadow, "loose.tgi"));

        foreach (var (exception, code) in new (Exception, string)[] {
            (new AggregateException(new HttpRequestException("raw", null, HttpStatusCode.NotFound)), "http-404"),
            (new HttpRequestException("raw", null, HttpStatusCode.Forbidden), "http-access"),
            (new HttpRequestException("raw", null, HttpStatusCode.TooManyRequests), "http-access"),
            (new HttpRequestException("raw", null, HttpStatusCode.ServiceUnavailable), "network"),
            (new AuthenticationException("TLS"), "security"), (new System.Security.Cryptography.CryptographicException("signature"), "security"),
            (new IOException("disk", unchecked((int)0x80070070)), "disk-full"),
            (new IOException("sharing", unchecked((int)0x80070020)), "locked"),
            (new UnauthorizedAccessException("denied"), "access"), (new TaskCanceledException(), "timeout"),
            (new InvalidDataException("SHA-256 mismatch"), "hash"), (new JsonException("bad json"), "format") })
        {
            var issue = FriendlyErrors.Describe(exception); Check(issue.Code == code, "Wrong friendly error: " + code);
            Check(issue.Body("ru").Length > 40 && issue.Body("en").Length > 40 && issue.ActionText("ru").Length > 4, "Missing error explanation/action");
        }

        var storageRoot = Path.Combine(root, "storage"); var cache = Path.Combine(storageRoot, "cache"); var storageGame = Path.Combine(storageRoot, "game");
        Directory.CreateDirectory(storageGame); var control = Path.Combine(storageGame, ".pawpatch"); Directory.CreateDirectory(control);
        File.WriteAllText(Path.Combine(storageGame, "save.k2s"), "save sentinel"); File.WriteAllText(Path.Combine(storageGame, "k2.exe"), "stock sentinel");
        string Write(string relative, string contents = "old data") { var path = Path.GetFullPath(Path.Combine(storageRoot, relative)); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, contents); return path; }
        var obsolete = Write($"cache/downloads/old/1/{H('B')}.zip");
        var current = Write($"cache/downloads/current/2/{H('A')}.zip");
        var pinned = Write($"cache/downloads/pinned/1/{H('C')}.zip");
        var rollbackCache = Write($"cache/downloads/rollback/1/{H('D')}.zip");
        var unknown = Write("cache/downloads/old/1/personal.txt");
        var partial = Write($"cache/downloads/old/1/{H('E')}.zip.download");
        var originals = Write("game/.pawpatch/originals/source.bin", "original sentinel");
        var launcherPrevious = Write("PawsPatchLauncher.exe.previous", "last launcher");
        var oldLauncher = Write($"cache/launcher/PawsPatchLauncher-0.4.0-{H('F')}.exe", "old launcher");
        var installed = new InstallState { Modules = new() { ["current"] = new() { Version = "2", ArchiveSha256 = H('A'), Enabled = true } } };
        File.WriteAllText(Path.Combine(control, "state.json"), JsonSerializer.Serialize(installed, LauncherJsonContext.Default.InstallState));
        string Transaction(string phase, bool before = false)
        {
            var dir = Path.Combine(control, "transactions", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
            var journal = new PatchTransaction { Phase = phase, Before = before ? new() { Modules = new() { ["rollback"] = new() { Version = "1", ArchiveSha256 = H('D') } } } : new() };
            File.WriteAllText(Path.Combine(dir, "journal.json"), JsonSerializer.Serialize(journal, LauncherJsonContext.Default.PatchTransaction)); return dir;
        }
        var oldBackup = Transaction("complete"); var rollback = Transaction("complete", true); var pending = Transaction("prepared");
        File.WriteAllText(Path.Combine(control, "rollback.txt"), Path.GetFileName(rollback));
        var stalePackage = Path.Combine(control, "packages", "old", "1"); Directory.CreateDirectory(Path.Combine(stalePackage, "payload"));
        File.WriteAllText(Path.Combine(stalePackage, ".verified"), H('B'));
        File.WriteAllText(Path.Combine(stalePackage, "module.json"), JsonSerializer.Serialize(new ModuleArchiveManifest { Id = "old", Version = "1", Files = [new ModuleFile { Path = "old.tgi", Sha256 = H('A') }] }, LauncherJsonContext.Default.ModuleArchiveManifest));
        File.WriteAllText(Path.Combine(stalePackage, "payload", "old.tgi"), "cache");
        void AgeTree(string path) { foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddDays(-20)); foreach(var d in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)) Directory.SetLastWriteTimeUtc(d, DateTime.UtcNow.AddDays(-20)); }
        AgeTree(storageRoot);
        Directory.SetCreationTimeUtc(oldBackup, DateTime.UtcNow.AddDays(-30)); Directory.SetCreationTimeUtc(rollback, DateTime.UtcNow.AddDays(-20)); Directory.SetCreationTimeUtc(pending, DateTime.UtcNow.AddDays(-10));
        var options = new StorageOptions(cache, storageGame, Path.Combine(storageRoot, "PawsPatchLauncher.exe"), [new PackageRelease { Id = "pinned", Version = "1", Sha256 = H('C') }], []);
        var plan = StorageMaintenance.Scan(options);
        bool Eligible(string path) => plan.Entries.Single(x => x.Path == path).Cleanable;
        Check(Eligible(obsolete) && Eligible(partial) && Eligible(stalePackage) && Eligible(oldBackup), "Old managed data not cleanable");
        Check(!Eligible(current) && !Eligible(pinned) && !Eligible(rollbackCache), "Required caches became cleanable");
        Check(!Eligible(unknown) && !Eligible(Path.GetDirectoryName(originals)!) && !Eligible(rollback) && !Eligible(pending) && !Eligible(launcherPrevious), "Protected data became cleanable");
        File.Delete(Path.Combine(control, "rollback.txt"));
        Check(!StorageMaintenance.Scan(options).Entries.Single(x => x.Path == rollback).Cleanable, "Pending folder displaced last completed backup without a pointer");
        File.WriteAllText(Path.Combine(control, "rollback.txt"), Path.GetFileName(rollback));
        Check(Eligible(oldLauncher), "Obsolete launcher download retained unexpectedly");
        Check(!StorageMaintenance.Scan(options with { LauncherExe = oldLauncher }).Entries.Single(x => x.Path == oldLauncher).Cleanable, "Running launcher could be cleaned");
        var unknownPackageFile = Path.Combine(stalePackage, "personal.txt"); File.WriteAllText(unknownPackageFile, "keep"); AgeTree(storageRoot);
        Check(!StorageMaintenance.Scan(options).Entries.Single(x => x.Path == stalePackage).Cleanable, "Unrecognized package contents would be deleted");
        File.Delete(unknownPackageFile); AgeTree(storageRoot);
        plan = StorageMaintenance.Scan(options);
        // Changing the pointer after approval protects the now-selected backup.
        File.WriteAllText(Path.Combine(control, "rollback.txt"), Path.GetFileName(oldBackup));
        File.AppendAllText(obsolete, "changed after approval");
        var result = StorageMaintenance.Clean(options, plan, true, true);
        Check(File.Exists(obsolete) && Directory.Exists(oldBackup), "Changed file/current pointer was ignored");
        Check(!File.Exists(partial) && !Directory.Exists(stalePackage) && result.Skipped >= 2, "Cleanup did not handle eligible/stale items");
        Check(File.Exists(current) && File.Exists(pinned) && File.Exists(rollbackCache) && File.ReadAllText(originals) == "original sentinel", "Cleanup damaged protected caches or originals");
        Check(File.ReadAllText(Path.Combine(storageGame, "save.k2s")) == "save sentinel" && File.ReadAllText(Path.Combine(storageGame, "k2.exe")) == "stock sentinel", "Cleanup touched live game/saves");
        var external = Write("outside.txt", "outside sentinel");
        var forged = new StoragePlan([new("downloads", external, 10, true, "forged")]);
        Check(StorageMaintenance.Clean(options, forged, true, true).Skipped == 1 && File.Exists(external), "Forged cleanup target escaped allowed roots");
        var none = StorageMaintenance.Clean(options, StorageMaintenance.Scan(options), false, false);
        Check(none.Removed == 0, "Unchecked categories were deleted");
        File.WriteAllText(Path.Combine(control, "rollback.txt"), "..\\outside");
        Reject(() => StorageMaintenance.Scan(options));
        File.WriteAllText(Path.Combine(control, "rollback.txt"), Path.GetFileName(oldBackup));
        var link = Path.Combine(cache, "downloads", "linked");
        try
        {
            // Junctions can be created without enabling Windows Developer Mode.
            var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + storageGame.Replace("'", "''") + "' | Out-Null");
            using var create = System.Diagnostics.Process.Start(start)!;
            await create.WaitForExitAsync();
            if (create.ExitCode != 0) throw new IOException("Fixture junction creation failed: " + await create.StandardError.ReadToEndAsync());
            Reject(() => StorageMaintenance.Scan(options));
        }
        finally { if (Directory.Exists(link)) Directory.Delete(link); }

        var clock = new ManualClock(); var feedback = new OperationFeedback(clock);
        feedback.Begin(() => "working"); Check(feedback.Working && feedback.Message == "working", "Working status missing");
        feedback.Show(() => "done"); feedback.Finish(); Check(feedback.Message == "done" && !feedback.Working, "Completion lost");
        clock.Now += TimeSpan.FromSeconds(7); Check(feedback.Message is null, "Completed status did not expire");
        feedback.Show(() => "error", true); clock.Now += TimeSpan.FromDays(1); Check(feedback.Failed && feedback.Message == "error", "Actionable error expired");
        feedback.Begin(() => "retry"); Check(!feedback.Failed && feedback.Message == "retry", "Retry kept old error");
        feedback.Finish(); Check(feedback.Message is null, "Working status remained after finish");
        feedback.Show(() => "toast failure", true, TimeSpan.FromSeconds(8), expireFailure: true);
        Check(feedback.Failed && feedback.HasExpiry, "Transient failure is not timed");
        clock.Now += TimeSpan.FromSeconds(7); Check(feedback.Message == "toast failure", "Failure vanished too soon");
        clock.Now += TimeSpan.FromSeconds(1); Check(feedback.Message is null, "Transient failure did not expire");
        feedback.Show(() => "toast success", duration: TimeSpan.FromSeconds(3));
        clock.Now += TimeSpan.FromSeconds(3); Check(feedback.Message is null, "Short success did not expire");
        Console.WriteLine($"ENHANCEMENTS PASS {count}: report comparison/import, error actions, scoped cleanup, status lifecycle");
        return count;
    }
    private sealed class ManualClock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
