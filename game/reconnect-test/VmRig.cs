// One fixed PC/VirtualBox recovery scenario. No remote shell or arbitrary commands.
// Run --host on the PC and without arguments in the NAT guest from the same
// fresh shared folder, with a verified r2 kit in its kit/ subdirectory.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

sealed class VmRecoveryRig : Form
{
    readonly string shared = AppDomain.CurrentDomain.BaseDirectory;
    readonly bool host;
    readonly Label status = new Label { Dock = DockStyle.Fill, Padding = new Padding(16), Text = "Preparing private recovery test..." };
    string root, gameDir, data, role;
    Process game;
    bool finished;
    VmRecoveryRig(bool isHost)
    {
        host = isHost; role = host ? "host" : "guest";
        Text = "Paw recovery r2 - PC / VM regression - " + role;
        Width = 650; Height = 180; Controls.Add(status);
        Shown += async delegate { await Task.Run((Action)Run); finished = true; };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!finished) e.Cancel = true; };
    }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.Run(new VmRecoveryRig(args.SequenceEqual(new[] { "--host" })));
    }
    static string Hash(string path)
    {
        using (var h = SHA256.Create()) using (var s = File.OpenRead(path)) return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
    }
    string Signal(string name) { return Path.Combine(shared, name + ".txt"); }
    void Mark(string name, string text) { File.WriteAllText(Signal(name), text); }
    bool Has(string name) { return File.Exists(Signal(name)); }
    void Report(string message)
    {
        File.AppendAllText(Path.Combine(shared, role + "-events.log"), DateTime.Now.ToString("O") + " " + message + "\n");
        BeginInvoke((Action)(() => status.Text = message));
    }
    string NativeLog()
    {
        try { return ReadShared(Path.Combine(gameDir, "Mods", "plugins.log")); }
        catch (IOException) { return ""; }
    }
    static string ReadShared(string path)
    {
        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var r = new StreamReader(f)) return r.ReadToEnd();
    }
    void Wait(Func<bool> ready, int seconds, string label)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (!ready())
        {
            if (Has(host ? "guest-failed" : "host-failed")) throw new IOException("Other test peer failed: " + label);
            if (game != null && game.HasExited) throw new IOException("Owned game exited " + game.ExitCode + ": " + label);
            if (DateTime.UtcNow > until) throw new TimeoutException(label);
            Thread.Sleep(500);
        }
        Report(label);
    }
    void Prepare()
    {
        if (Has(role + "-started")) throw new IOException("Use a fresh shared test folder.");
        Mark(role + "-started", Environment.MachineName);
        if (Process.GetProcessesByName("k2").Any()) throw new IOException("Close the existing game before this private test.");
        if (!Process.GetProcessesByName("steam").Any()) throw new IOException("Steam must be running.");
        root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawVM-r2-" + role + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        gameDir = Path.Combine(root, "game"); data = Path.Combine(root, "docs", "Kohan2", "data");
        var json = new JavaScriptSerializer();
        var manifest = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(shared, "kit", "manifest.json")));
        if ((string)manifest["build"] != "recovery-r2-1372") throw new IOException("Wrong kit revision");
        foreach (var file in (Dictionary<string, object>)manifest["files"])
        {
            string relative = file.Key.Replace('/', '\\');
            if (!relative.StartsWith("game\\", StringComparison.Ordinal) || relative.Split('\\').Contains("..") || relative.Contains(':')) throw new IOException("Unexpected kit path");
            string from = Path.Combine(shared, "kit", relative), to = Path.Combine(root, relative);
            if (Hash(from) != (string)file.Value) throw new IOException("Kit hash mismatch: " + relative);
            Directory.CreateDirectory(Path.GetDirectoryName(to)); File.Copy(from, to);
            if (Hash(to) != (string)file.Value) throw new IOException("Copy mismatch: " + relative);
        }
        Directory.CreateDirectory(Path.Combine(data, "User")); Directory.CreateDirectory(Path.Combine(data, "Save"));
        Directory.CreateDirectory(Path.Combine(gameDir, "startup"));
        File.WriteAllText(Path.Combine(data, "User", "UVars.tgi"), "[Vars]\n{\n int ResolutionX = 800\n int ResolutionY = 600\n flag ResolutionWindowed = true\n flag ResolutionCoopFullscreenMode = false\n flag MouseClipCursor = false\n flag RunWhenInactive = true\n flag FirstRun = false\n float AudioMainVolume = 0\n float FrameRateLimit = 30\n}\n", Encoding.Unicode);
        File.WriteAllText(Path.Combine(gameDir, "startup", "autoexec.txt"), "adddepot data.rwd\nadddepot music.rwd\nadddepot test-data/\nadddepot %USERDATA%/data/ 1\n");
        Mark(role + "-prepared", root); Report("Private files verified; waiting for the other test peer.");
    }
    void Launch()
    {
        string script = host
            ? "Wait 45\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\nLobbySetLANNickname PawVMHost\nWait 2\nInternetGameCreate PawVM 2 2\nWait 3\nSelectLaunchType RANDOM_MAP\nWait 90\nStagingLaunch\nWait 200\nStagingLaunch\nWait 25\nStagingLaunch\nWait 200\nReallyQuit\n"
            : "Wait 55\nRequestMultiplayer\nWait 3\nRequestLobbyLAN\nWait 3\nLobbySetLANNickname PawVMClient\nWait 2\nLobbyJoinDirect\nWait 2\nJoinDirectJoin\nWait 15\nStagingReady 0\nWait 20\nStagingReady 0\nWait 400\nReallyQuit\n";
        File.WriteAllText(Path.Combine(gameDir, "Mods", "rig.txt"), script, Encoding.ASCII);
        var start = new ProcessStartInfo(Path.Combine(gameDir, "k2.exe"), "+Exec Mods/rig.txt") { WorkingDirectory = gameDir, UseShellExecute = false };
        start.EnvironmentVariables["SteamAppId"] = "97130"; start.EnvironmentVariables["SteamGameId"] = "97130";
        start.EnvironmentVariables["PAWS_RECOVERY_DATA"] = data;
        start.EnvironmentVariables["PAWS_TEST_DOCUMENTS"] = Path.Combine(root, "docs");
        start.EnvironmentVariables["PAWS_RECOVERY_AUTO_VOTE"] = "1";
        if (!host) start.EnvironmentVariables["PAWS_TEST_JOIN"] = "10.0.2.2:5860";
        else start.EnvironmentVariables.Remove("PAWS_TEST_JOIN");
        game = Process.Start(start); Report("Started owned game PID " + game.Id);
        Wait(() => NativeLog().Contains("process " + game.Id + ",") && NativeLog().Contains("READY r2:"), 45, "Current process recovery hooks verified");
    }
    void StopOwned()
    {
        if (game == null) return;
        if (!game.HasExited) { game.Kill(); game.WaitForExit(10000); }
        game.Dispose(); game = null;
    }
    void CopyEvidence()
    {
        if (gameDir == null || !Directory.Exists(gameDir)) return;
        var output = Path.Combine(shared, role + "-evidence"); Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "plugins.log"), NativeLog());
        foreach (var name in new[] { "Logs", "Mods" })
            foreach (var log in Directory.GetFiles(Path.Combine(gameDir, name), "*.log"))
                File.WriteAllText(Path.Combine(output, name + "-" + Path.GetFileName(log)), ReadShared(log));
        foreach (var save in Directory.GetFiles(Path.Combine(data, "Save"), "Paw Resume*.RSG"))
            File.Copy(save, Path.Combine(output, Path.GetFileName(save)), true);
    }
    void Run()
    {
        try
        {
            Prepare(); Wait(() => Has(host ? "guest-prepared" : "host-launched"), 300, "Other peer ready");
            Launch(); if (host) Mark("host-launched", "1");
            Wait(() => NativeLog().Contains("MATCH_ROLE: original_host=" + (host ? "1" : "0")), 260, "Entered network match");
            Mark(role + "-joined", "1"); Wait(() => Has(host ? "guest-joined" : "host-joined"), 30, "Both peers joined");
            if (host)
            {
                Thread.Sleep(10000); Mark("drop-client", "1");
                Wait(() => NativeLog().Contains("RESUME_READY:"), 90, "Save ready in lobby"); Mark("rejoin-client", "1");
            }
            else
            {
                Wait(() => Has("drop-client"), 90, "Simulating client crash"); CopyEvidence(); StopOwned();
                Wait(() => Has("rejoin-client"), 90, "Host ready for rejoin"); Thread.Sleep(5000); Launch();
            }
            Wait(() => host ? NativeLog().Split(new[] { "MATCH_ROLE: original_host=1" }, StringSplitOptions.None).Length >= 3 : NativeLog().Contains("MATCH_ROLE: original_host=0"), 260, "Re-entered saved match");
            Mark(role + "-resumed", "1"); Wait(() => Has(host ? "guest-resumed" : "host-resumed"), 40, "Both peers resumed");
            Thread.Sleep(20000); CopyEvidence();
            foreach (var log in Directory.GetFiles(Path.Combine(gameDir, "Logs"), "*.log"))
            {
                string text = ReadShared(log).ToLowerInvariant();
                if (new[] { "out of sync", "out-of-sync", "assert:", "unhandled exception" }.Any(text.Contains)) throw new IOException("Native error: " + log);
            }
            if (game.HasExited) throw new IOException("Game exited after resume");
            Mark(role + "-passed", "1"); Wait(() => Has(host ? "guest-passed" : "host-passed"), 40, "Both peers passed");
            Report("PASS. Evidence copied to the shared test folder.");
        }
        catch (Exception e) { Mark(role + "-failed", e.ToString()); Report("FAILED: " + e.Message); }
        finally { StopOwned(); try { CopyEvidence(); } catch (Exception e) { Report("Evidence: " + e.Message); } }
    }
}
