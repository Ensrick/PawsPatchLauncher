using System.Security.Cryptography;
using System.Text;
using PawsPatchLauncher;

// The sandbox copy (GameSandbox): what is copied, refreshed and removed, and that the game folder is never written.
internal static class GameSandboxTests
{
    public static async Task<int> RunAsync(string root)
    {
        int passed = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Sandbox: " + why); passed++; }
        async Task Reject(Func<Task> action, string why)
        { try { await action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException) { passed++; return; } throw new Exception("Sandbox should reject: " + why); }

        root = Path.GetFullPath(root);
        var game = Path.Combine(root, "sandbox-game");
        var copy = Path.Combine(root, "sandbox-copy");
        void Write(string relative, string text) { var path = Path.Combine(game, relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text); }
        Write("k2.exe", "fixture executable");
        Write("Data.rwd", "data");
        Write("startup\\autoexec.txt", "adddepot Data.rwd\r\n");
        Write("Local_base_en\\Hotkeys.txt", "keys");
        Write(".pawpatch\\state.json", "{}");
        Write("Logs\\log-1.log", "game log");
        Write("shfolder.dll", "someone else's stand-in");
        var exe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("fixture executable")));
        string Snapshot() => string.Join("|", Directory.GetFiles(game, "*", SearchOption.AllDirectories).Order()
            .Select(f => f + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))) + ":" + File.GetLastWriteTimeUtc(f).Ticks));
        var before = Snapshot();

        var dll = GameSandbox.Dll();
        Check(dll.Length > 10_000 && dll[0] == 'M' && dll[1] == 'Z', "the embedded sandbox module is a PE image");
        Check(GameSandbox.SupportedK2ExeSha256 == "CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A", "only the 1.3.68 public executable is supported");

        var sandbox = new GameSandbox(game, copy, exe);
        Check(await sandbox.SyncAsync() == 4, "the first sync copies every game file");
        Check(File.ReadAllText(Path.Combine(copy, "startup", "autoexec.txt")) == "adddepot Data.rwd\r\n"
            && File.Exists(Path.Combine(copy, "Local_base_en", "Hotkeys.txt")) && File.Exists(sandbox.Executable), "files and folders are copied");
        Check(!Directory.Exists(Path.Combine(copy, ".pawpatch")) && !Directory.Exists(Path.Combine(copy, "Logs")), "launcher state and game logs stay out of the copy");
        Check(File.ReadAllBytes(Path.Combine(copy, GameSandbox.DllName)).SequenceEqual(dll), "the copy gets the sandbox module, not the game folder's file");
        Check(File.GetLastWriteTimeUtc(Path.Combine(copy, "Data.rwd")) == File.GetLastWriteTimeUtc(Path.Combine(game, "Data.rwd")), "copies keep the source time");
        Check(Snapshot() == before, "the game folder is unchanged");
        Check(await sandbox.SyncAsync() == 0, "an unchanged game folder copies nothing");

        // Changes in the game folder reach the copy; the copied game's own files stay.
        File.WriteAllText(Path.Combine(copy, "sandbox.log"), "written by the copied game");
        Directory.CreateDirectory(Path.Combine(copy, "Logs")); File.WriteAllText(Path.Combine(copy, "Logs", "log-0.log"), "copy log");
        Write("Data.rwd", "data, updated by an Apply");
        Write("PawsCustom\\mod.rwd", "a mounted mod");
        File.Delete(Path.Combine(game, "Local_base_en", "Hotkeys.txt"));
        Check(await sandbox.SyncAsync() == 2, "only the changed and new files are copied");
        Check(File.ReadAllText(Path.Combine(copy, "Data.rwd")) == "data, updated by an Apply" && File.Exists(Path.Combine(copy, "PawsCustom", "mod.rwd")), "changed and new files arrive");
        Check(!File.Exists(Path.Combine(copy, "Local_base_en", "Hotkeys.txt")), "a file the game folder no longer has leaves the copy");
        Check(File.Exists(Path.Combine(copy, "sandbox.log")) && File.Exists(Path.Combine(copy, "Logs", "log-0.log")), "files the copy did not place are kept");

        // A copy changed behind the launcher's back is repaired, and a damaged file list only means a full copy.
        File.WriteAllText(Path.Combine(copy, "Data.rwd"), "x");
        File.WriteAllBytes(Path.Combine(copy, GameSandbox.DllName), [1, 2, 3]);
        Check(await sandbox.SyncAsync() == 1 && File.ReadAllText(Path.Combine(copy, "Data.rwd")) == "data, updated by an Apply"
            && File.ReadAllBytes(Path.Combine(copy, GameSandbox.DllName)).SequenceEqual(dll), "a resized copy file and a changed module are restored");
        File.WriteAllText(Path.Combine(copy, ".k2sandbox-files.json"), "{ not json");
        Check(await sandbox.SyncAsync() == 4, "a damaged file list copies everything again");

        // Refusals: another executable, a copy inside the game folder or the game inside the copy.
        var before2 = Snapshot();
        await Reject(() => new GameSandbox(game, Path.Combine(root, "other-copy"), new string('0', 64)).SyncAsync(), "an executable other than the supported one");
        Check(!Directory.Exists(Path.Combine(root, "other-copy")), "a refused executable creates no copy");
        await Reject(() => new GameSandbox(game, Path.Combine(game, "Sandbox game"), exe).SyncAsync(), "a copy inside the game folder");
        await Reject(() => new GameSandbox(game, game, exe).SyncAsync(), "the game folder itself as the copy");
        await Reject(() => new GameSandbox(game, root, exe).SyncAsync(), "a copy that contains the game folder");
        File.WriteAllText(sandbox.Executable, "fixture executablX");
        File.SetLastWriteTimeUtc(sandbox.Executable, File.GetLastWriteTimeUtc(Path.Combine(game, "k2.exe")));
        await Reject(() => sandbox.SyncAsync(), "a copied executable that differs from the game's at the same size and time");
        Check(Snapshot() == before2, "refusals leave the game folder unchanged");

        // The start request: the copy's executable in the copy, with Steam's app id and without test switches.
        Environment.SetEnvironmentVariable("K2SB_TEST_SWITCH", "2");
        try
        {
            var start = sandbox.StartInfo();
            Check(start.FileName == Path.Combine(copy, "k2.exe") && start.WorkingDirectory == copy && !start.UseShellExecute, "the copy starts in its own folder");
            Check(start.Environment["SteamAppId"] == GameSandbox.SteamAppId && start.Environment["SteamGameId"] == GameSandbox.SteamAppId
                && !start.Environment.ContainsKey("K2SB_TEST_SWITCH"), "the start carries the Steam app id and no sandbox test switches");
        }
        finally { Environment.SetEnvironmentVariable("K2SB_TEST_SWITCH", null); }
        Console.WriteLine($"SANDBOX PASS {passed}: copy, refresh, removal of placed files only, module, refusals, start request; game folder untouched.");
        return passed;
    }
}
