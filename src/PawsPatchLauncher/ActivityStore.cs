using System.Diagnostics;
using System.Text.Json;

namespace PawsPatchLauncher;

public sealed class RunRecord
{
    public int ProcessId { get; set; }
    public long StartTicks { get; set; }
    public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
    public bool CleanExit { get; set; }
    public bool ReachedWindow { get; set; }
    public int? ExitCode { get; set; }
    public string GameRoot { get; set; } = "";
    public UserSettings? Settings { get; set; }
    public string? ReleaseId { get; set; }
}

public sealed class LastWorkingConfiguration
{
    public UserSettings Settings { get; set; } = new();
    public string? ReleaseId { get; set; }
    public string SavedAt { get; set; } = "";
}

public static class ActivityStore
{
    public static bool IsCustomModsTest { get; } = CustomModsTestMarker();
    private static bool CustomModsTestMarker()
    {
        try { return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "launcher.test-mode")).Trim() == "custom-rwd"; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public static string? LocalTestProfile => Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--test-profile=", StringComparison.Ordinal))?["--test-profile=".Length..]
        ?? (File.Exists(Path.Combine(AppContext.BaseDirectory, "launcher.test-mode")) ? Path.Combine(AppContext.BaseDirectory, "test-profile") : null);
    public static string Root => LocalTestProfile is { Length: > 0 } profile ? Path.GetFullPath(profile) : IsSmokeTest
        ? Path.Combine(Path.GetTempPath(), "PawsPatchLauncherSmoke", Environment.ProcessId.ToString())
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawsPatchLauncher");
    public static bool IsSmokeTest => Environment.GetCommandLineArgs().Contains("--smoke-test");
    private static string PathFor(string name) => Path.Combine(Root, name + ".json");
    public static RunRecord? Read(string name)
    {
        try { return File.Exists(PathFor(name)) ? JsonSerializer.Deserialize(File.ReadAllText(PathFor(name)), LauncherJsonContext.Default.RunRecord) : null; }
        catch { return null; }
    }
    public static void Save(string name, RunRecord record)
    {
        Directory.CreateDirectory(Root);
        var path = PathFor(name);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(record, LauncherJsonContext.Default.RunRecord));
        File.Move(path + ".tmp", path, true);
    }
    public static bool IsAlive(RunRecord record)
    {
        try { using var process = Process.GetProcessById(record.ProcessId); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == record.StartTicks; }
        catch { return false; }
    }
    public static RunRecord ForProcess(Process process) => new() { ProcessId = process.Id, StartTicks = process.StartTime.ToUniversalTime().Ticks };
    public static void Log(Exception error)
    {
        ActionJournal.Record("error", error.GetType().Name);
        try { Directory.CreateDirectory(Root); File.AppendAllText(Path.Combine(Root, "launcher-errors.log"), $"{DateTimeOffset.UtcNow:O} {error}\n"); } catch { }
    }
    public static LastWorkingConfiguration? Working(string game)
    {
        var path = Path.Combine(game, ".pawpatch", "last-working.json");
        return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), LauncherJsonContext.Default.LastWorkingConfiguration) : null;
    }
    public static async Task SaveWorkingAsync(RunRecord record)
    {
        if (record.Settings is null) return;
        var working = new LastWorkingConfiguration { Settings = record.Settings, ReleaseId = record.ReleaseId, SavedAt = DateTimeOffset.UtcNow.ToString("O") };
        await CryptoAndIO.AtomicWriteTextAsync(Path.Combine(record.GameRoot, ".pawpatch", "last-working.json"), JsonSerializer.Serialize(working, LauncherJsonContext.Default.LastWorkingConfiguration));
    }
}
