using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

// Explicit opt-in integration check. Requires a prepared disposable game and its
// own Documents profile; never discovers or edits the player's installation.
internal static class LobbyLaunchChecks
{
    internal static void Run(string root)
    {
        root = Path.GetFullPath(root);
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null ||
            !File.Exists(Path.Combine(root, "lobby-fixture.marker")))
            throw new InvalidOperationException("Disposable smoke profile and explicit lobby fixture required.");
        var game = Path.Combine(root, "game");
        var documents = Path.Combine(root, "Documents");
        var exe = Path.Combine(game, "k2.exe");
        if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))) !=
            "CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A")
            throw new InvalidOperationException("Expected original Steam 1.3.68.");
        if (Process.GetProcessesByName("k2").Length != 0) throw new InvalidOperationException("Close the game before this fixture.");
        var prefs = new[] { Path.Combine(documents, "Kohan2", "data", "User", "Preferences.rup"),
            Path.Combine(game, "Userdata", "User", "Preferences.rup") };
        var seed = File.ReadAllBytes(prefs[0]);
        var source = Chunks(seed);
        var marked = source.Select(c => (c.Name, c.Header.ToArray(), c.Data.ToArray())).ToList();
        var n = marked.FindIndex(c => c.Name == "SingleLaunchSettings");
        var marker = Encoding.Unicode.GetBytes("kingdom16");
        marked[n] = (marked[n].Name, marked[n].Item2, marked[n].Item3.Concat(new byte[] { 9, 0 }).Concat(marker).ToArray());
        var input = Pack(marked);
        foreach (var path in prefs) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, input); }
        File.WriteAllBytes(Path.Combine(root, "marked-preferences.rup"), input);
        var repairType = typeof(MainWindow).Assembly.GetType("PawsPatchLauncher.GameLobbyPreferences")!;
        var repair = repairType.GetMethod("PrepareForLaunchAsync", BindingFlags.NonPublic | BindingFlags.Static,
            null, [typeof(string), typeof(UserSettings), typeof(string)], null)!;
        using var http = new HttpClient(new NoNetwork());
        var config = new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [], CacheRoot = Path.Combine(root, "cache") };
        var settings = new UserSettings { Mod = GameMod.Vanilla, VanillaPawPatchEnabled = false, PawPatchEnabled = false, ModNoticeSeen = true };
        new SettingsStore().Save(settings);
        var window = new MainWindow(config, new FeedClient(config, http));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Process? process = null;
        int prepared = 0, started = 0;
        async Task Prepare(string selectedGame, UserSettings applied)
        {
            Check(selectedGame == game && applied.Mod == GameMod.Vanilla, "Wrong launch configuration");
            await (Task)repair.Invoke(null, [game, applied, documents])!;
            foreach (var path in prefs)
            {
                var after = Chunks(File.ReadAllBytes(path));
                foreach (var chunk in source.Where(c => c.Name is not ("SingleLaunchSettings" or "RMCPreferences")))
                    Check(after.Single(c => c.Name == chunk.Name).Data.SequenceEqual(chunk.Data), "Unrelated preference changed: " + chunk.Name);
                var single = after.Single(c => c.Name == "SingleLaunchSettings").Data;
                Check(Convert.ToHexString(SHA256.HashData(single)) == "53AD4CD6CE73A453F2881835B064112021D9097B2BB96B8B6C2C1E8F42A0DB04", "Wrong 1.3.68 defaults");
                var backups = Directory.GetFiles(Path.Combine(Path.GetDirectoryName(path)!, "PawsLauncherBackups"));
                Check(backups.Any(p => File.ReadAllBytes(p).SequenceEqual(input)), "Original backup differs");
            }
            prepared++;
        }
        Set("_settings", settings);
        Set("_game", new GameInstallation(game, exe, "lobby-fixture", "stable"));
        Set("_patchInstalled", true); Set("_settingsPending", false); Set("_busy", false);
        Set("_prepareGameLobby", new Func<string, UserSettings, Task>(Prepare));
        Set("_startGameProcess", new Func<ProcessStartInfo, Process?>(info =>
        {
            Check(prepared == 1 && info.FileName == exe && info.WorkingDirectory == game, "Launch preceded repair");
            info.UseShellExecute = false; info.WindowStyle = ProcessWindowStyle.Normal;
            info.Arguments = "+Exec startup/lobby-match.txt";
            info.Environment["SteamAppId"] = "97130"; info.Environment["SteamGameId"] = "97130";
            info.Environment["K2_RIG_DOCUMENTS"] = documents; info.Environment.Remove("K2_RIG_JOIN");
            started++; return process = Process.Start(info);
        }));
        async Task Scenario()
        {
            await (Task)typeof(MainWindow).GetMethod("LaunchGameAsync", flags)!.Invoke(window, null)!;
            Check(prepared == 1 && started == 1 && process is not null, "Launcher did not reach native start after repair");
            // Preserve the production no-running-game rule for the repeated preflight.
            await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(140));
            Check(process.ExitCode == 0, "Native game exited with an error");
            var logs = Directory.GetFiles(Path.Combine(game, "Logs"), "log-*-ok.log")
                .Where(p => File.GetLastWriteTimeUtc(p) >= process.StartTime.ToUniversalTime()).ToArray();
            Check(logs.Length == 1 && File.ReadAllText(logs[0]).Contains("LOBBY_REPAIR_MATCH_PASS"), "Game did not reach the test match");
            Check(!File.ReadAllLines(logs[0]).Any(l => l.StartsWith("ERROR:") || l.Contains("ASSERT:")), "Native match logged a failure");
            var beforeRepeat = prefs.Select(File.ReadAllBytes).ToArray();
            var backupCounts = prefs.Select(p => Directory.GetFiles(Path.Combine(Path.GetDirectoryName(p)!, "PawsLauncherBackups")).Length).ToArray();
            await (Task)repair.Invoke(null, [game, settings, documents])!;
            for (int i = 0; i < prefs.Length; i++)
            {
                Check(File.ReadAllBytes(prefs[i]).SequenceEqual(beforeRepeat[i]), "Repeated preflight changed clean preferences");
                Check(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(prefs[i])!, "PawsLauncherBackups")).Length == backupCounts[i], "Repeated preflight created another backup");
            }
            Console.WriteLine("LOBBY LAUNCH PASS: real launcher Play path, original 1.3.68, both preference locations repaired before process start, backups and unrelated chunks preserved, native match and normal exit, repeated preflight unchanged.");
        }
        try
        {
            var task = Dispatcher.CurrentDispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
        }
        finally { if (process is not null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } process.Dispose(); } window.Close(); }
    }

    private static List<(string Name, byte[] Header, byte[] Data)> Chunks(byte[] bytes)
    {
        var count = BitConverter.ToInt32(bytes, 8); var result = new List<(string, byte[], byte[])>();
        for (int i = 0; i < count; i++)
        {
            var header = bytes.AsSpan(bytes.Length - count * 96 + i * 96, 96).ToArray();
            result.Add((Encoding.Unicode.GetString(header, 0, 64).TrimEnd('\0'), header,
                bytes.AsSpan((int)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(64)), (int)BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(72))).ToArray()));
        }
        return result;
    }
    private static byte[] Pack(List<(string Name, byte[] Header, byte[] Data)> chunks)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write("TGCK"u8); writer.Write(2); writer.Write(chunks.Count);
        foreach (var c in chunks) { BinaryPrimitives.WriteUInt64LittleEndian(c.Header.AsSpan(64), (ulong)output.Position); BinaryPrimitives.WriteUInt64LittleEndian(c.Header.AsSpan(72), (ulong)c.Data.Length); BinaryPrimitives.WriteUInt64LittleEndian(c.Header.AsSpan(88), (ulong)c.Data.Length); writer.Write(c.Data); }
        foreach (var c in chunks) writer.Write(c.Header);
        return output.ToArray();
    }
    private sealed class NoNetwork : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new InvalidOperationException("Network disabled in lobby fixture"); }
}
