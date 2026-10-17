using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PawsPatchLauncher;

public static class DiagnosticsCollector
{
    private static async Task CopyLauncherDiagnosticsAsync(string staging, DiagnosticCollectionReport report, CancellationToken token)
    {
        foreach (var name in new[] { "launcher-errors.log", "self-update.log", "launcher-run.json", "previous-launcher-run.json", "game-run.json", "failed-launcher-sha256.txt", "update-rollback.txt", "user-actions.jsonl", "user-actions.1.jsonl", "user-actions.2.jsonl" })
        {
            var file = Path.Combine(ActivityStore.Root, name);
            if (File.Exists(file)) await GameDiagnosticFiles.CopyFileAsync(file, staging, "launcher/" + name, "launcher", report, token);
        }
    }

    public static Task<string> CreateLauncherOnlyAsync(string destination) => Task.Run(() => CreateLauncherOnlyCoreAsync(destination));

    private static async Task<string> CreateLauncherOnlyCoreAsync(string destination)
    {
        var staging = Path.Combine(Path.GetTempPath(), "PawsPatchDiagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            ActionJournal.Record("diagnostics.create", "launcher-only");
            await ActionJournal.FlushAsync();
            var report = new DiagnosticCollectionReport();
            await CopyLauncherDiagnosticsAsync(staging, report, default);
            await WriteTextAsync(staging, "system-information.json", await SystemDiagnostics.CollectAsync(null), default);
            await WriteTextAsync(staging, "launcher-report.txt", "Paw's Launcher diagnostics (game not selected).\nUser action history contains control names and game settings, not typed text or chat messages.\nHardware query failures are listed in system-information.json.\n", default);
            await GameDiagnosticFiles.WriteReportAsync(staging, report, default);
            await WriteHashManifestAsync(staging, default);
            await WriteArchiveAsync(staging, destination, default);
            ActionJournal.Record("diagnostics.created", "launcher-only");
            return destination;
        }
        finally { Directory.Delete(staging, true); }
    }
    public static Task<string> CreateAsync(
        string destination,
        GameInstallation game,
        UserSettings settings,
        InstallState state,
        IReadOnlyCollection<string> verificationErrors,
        CancellationToken cancellationToken = default,
        DiagnosticLocations? locations = null) => Task.Run(() => CreateCoreAsync(destination, game, settings, state,
            verificationErrors, cancellationToken, locations), cancellationToken);

    private static async Task<string> CreateCoreAsync(string destination, GameInstallation game, UserSettings settings,
        InstallState state, IReadOnlyCollection<string> verificationErrors, CancellationToken cancellationToken, DiagnosticLocations? locations)
    {
        var staging = Path.Combine(Path.GetTempPath(), "PawsPatchDiagnostics", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            ActionJournal.Record("diagnostics.create", "game");
            await ActionJournal.FlushAsync();
            await WriteTextAsync(staging, "system-information.json", await SystemDiagnostics.CollectAsync(game.Directory, cancellationToken), cancellationToken);
            var report = BuildReport(game, settings, state, verificationErrors);
            await WriteTextAsync(staging, "launcher-report.txt", report, cancellationToken);
            await WriteTextAsync(staging, "install-state.json",
                JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), cancellationToken);
            await WriteRuntimeInventoryAsync(staging, game, state, cancellationToken);

            var collection = new DiagnosticCollectionReport();
            await CopyLauncherDiagnosticsAsync(staging, collection, cancellationToken);
            foreach (var name in new[] { "state.json", "last-working.json", "rollback.txt", "mod-library.json" })
            {
                var file = Path.Combine(game.Directory, ".pawpatch", name);
                if (File.Exists(file)) await GameDiagnosticFiles.CopyFileAsync(file, staging, "launcher/" + name, "installation", collection, cancellationToken);
            }
            await GameDiagnosticFiles.CollectAsync(staging, locations ?? DiagnosticLocations.Current(game.Directory), cancellationToken, collection);

            await WriteHashManifestAsync(staging, cancellationToken);
            await WriteArchiveAsync(staging, destination, cancellationToken);
            ActionJournal.Record("diagnostics.created", "game");
            return destination;
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
        }
    }

    private static string BuildReport(GameInstallation game, UserSettings settings, InstallState state, IReadOnlyCollection<string> errors)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        var builder = new StringBuilder();
        builder.AppendLine("Paw's Launcher diagnostic archive");
        builder.AppendLine($"Created UTC: {DateTimeOffset.UtcNow:O}");
        builder.AppendLine($"Launcher: {version.Major}.{version.Minor}.{version.Build}");
        builder.AppendLine($"Configuration: {ConfigurationCode.Create(settings)}");
        builder.AppendLine($"Selected mod/channel: {settings.Mod}/{settings.Channel}; text: {(GameLanguages.Text(settings))}; speech: {GameLanguages.Voice(settings)}");
        builder.AppendLine($"Applied configuration: {(state.AppliedSettings is null ? "none" : ConfigurationCode.Create(state.AppliedSettings))}");
        builder.AppendLine($"Applied release: {state.ReleaseId ?? "none"}; applied base EXE SHA256: {state.BaseGameSha256 ?? "unknown"}");
        builder.AppendLine($"Game directory: {game.Directory}");
        builder.AppendLine($"Game branch: {game.Branch ?? "unknown"}");
        builder.AppendLine($"Steam build: {game.SteamBuild ?? "unknown"}");
        builder.AppendLine($"Verification: {(errors.Count == 0 ? "OK" : "FAILED")}");
        foreach (var error in errors) builder.AppendLine($"  {error}");
        builder.AppendLine();
        builder.AppendLine("Installed modules:");
        foreach (var module in state.Modules.OrderBy(x => x.Value.Priority).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            builder.AppendLine($"  {module.Key} {module.Value.Version} priority={module.Value.Priority} archive-sha256={module.Value.ArchiveSha256} files={module.Value.Files.Count}");
        builder.AppendLine();
        builder.AppendLine("Privacy note: crash dumps can contain fragments of process memory. Review the archive before sharing it publicly.");
        builder.AppendLine("User actions: last three logs (2 MiB each). Static control/action names, configuration changes and outcomes only; no typed text, passwords, codes or message contents. History begins with installation of this launcher build.");
        builder.AppendLine("Crash history: three newest readable log sets and ordinary game dumps, plus up to three Windows dumps and three Kohan WER reports. One newest full graphics/crash memory dump is included. AI action logs are excluded. Large text files retain their first 64 KiB and last 4 MiB. Companion logs and patch status are included. See collection-report.json for collected files, searched locations and failures. No dumps are created by this operation.");
        return builder.ToString();
    }

    private static async Task WriteRuntimeInventoryAsync(string staging, GameInstallation game, InstallState state, CancellationToken token)
    {
        var names = new[] { "k2.exe", "steam_api.dll", "steam_api64.dll", "d3d9.dll" }.Concat(state.Modules.Values.Where(m => m.Enabled)
            .SelectMany(m => m.Files).Where(f => GameCompatibilityPolicy.NativePath(f.Path)).Select(f => f.Path)).Distinct(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string> { "Actual runtime files: SHA-256  SIZE  MODIFIED-UTC  RELATIVE-PATH" };
        foreach (var name in names.Order(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var path = CryptoAndIO.SafeChildPath(game.Directory, name);
                if (!File.Exists(path)) { lines.Add("MISSING  " + name); continue; }
                var info = new FileInfo(path);
                lines.Add($"{await CryptoAndIO.Sha256Async(path, token)}  {info.Length}  {info.LastWriteTimeUtc:O}  {name}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException) { lines.Add("UNREADABLE " + error.GetType().Name + "  " + name); }
        }
        await WriteTextAsync(staging, "runtime-files.txt", string.Join(Environment.NewLine, lines), token);
    }

    private static async Task WriteArchiveAsync(string staging, string destination, CancellationToken token)
    {
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (var file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    var entry = archive.CreateEntry(Path.GetRelativePath(staging, file).Replace('\\', '/'), CompressionLevel.Optimal);
                    var modified = File.GetLastWriteTimeUtc(file);
                    if (modified.Year is >= 1980 and <= 2107) entry.LastWriteTime = new DateTimeOffset(modified);
                    using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    using var output = entry.Open();
                    await input.CopyToAsync(output, token);
                }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, destination, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task WriteHashManifestAsync(string root, CancellationToken cancellationToken)
    {
        var lines = new List<string> { "SHA-256  SIZE  PATH" };
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = await CryptoAndIO.Sha256Async(file, cancellationToken);
            lines.Add($"{hash}  {new FileInfo(file).Length}  {Path.GetRelativePath(root, file)}");
        }
        await WriteTextAsync(root, "files-sha256.txt", string.Join(Environment.NewLine, lines), cancellationToken);
    }

    private static async Task WriteTextAsync(string root, string relative, string text, CancellationToken cancellationToken)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, text, new UTF8Encoding(false), cancellationToken);
    }
}
