using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Local test kit only. Starts and stops only its own child in game/.
sealed class ReconnectTest : Form
{
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
    const string GameHash = "1eb79bbb678668be5a05f8c98103cd9988048490ce7c53c74c9a1d0813e9cd45";
    readonly ComboBox mode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    readonly Label status = new Label { AutoSize = false, Height = 60, Dock = DockStyle.Bottom, ForeColor = Color.LightSkyBlue, Padding = new Padding(14) };
    readonly Button launch = new Button { Text = "Запустить тестовую игру", AutoSize = true, Height = 36 };
    readonly Button drop = new Button { Text = "Имитировать обрыв этой игры", AutoSize = true, Enabled = false, Height = 36 };
    readonly Timer poll = new Timer { Interval = 1000 };
    readonly CheckBox autoVote = new CheckBox { Text = "Отключать потерявших связь игроков после 20 секунд ожидания", AutoSize = true };
    Process game;
    bool busy;
    bool exitReported, simulatedDrop;
    string currentProfile;
    static bool SharedSource()
    {
        return new Uri(Root + "\\").IsUnc || new DriveInfo(Path.GetPathRoot(Root)).DriveType == DriveType.Network;
    }

    static string Hash(string path)
    {
        using (var h = SHA256.Create()) using (var s = File.OpenRead(path))
            return BitConverter.ToString(h.ComputeHash(s)).Replace("-", "").ToLowerInvariant();
    }
    static Dictionary<string, object> Files()
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
        var m = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        if ((string)m["build"] != "recovery-r2-1372") throw new IOException("Неизвестная сборка теста.");
        return (Dictionary<string, object>)m["files"];
    }
    static string SafePath(string root, string name)
    {
        string p = Path.GetFullPath(Path.Combine(root, name.Replace('/', '\\')));
        if (!p.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Некорректный путь в комплекте теста.");
        return p;
    }
    static string InstallPrivateCopy(Action<string> progress)
    {
        string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawsReconnectTest-r2");
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !File.Exists(Path.Combine(target, "reconnect-test.marker")))
            throw new IOException("Папка для теста уже занята: " + target);
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "reconnect-test.marker"), "Paws reconnect local test r2\n");
        var files = Files(); int i = 0;
        foreach (var pair in files)
        {
            string source = SafePath(Root, pair.Key), dest = SafePath(target, pair.Key);
            progress("Подготовка отдельной копии: " + (++i) + " / " + files.Count);
            if (File.Exists(dest) && Hash(dest) == (string)pair.Value) continue;
            if (Hash(source) != (string)pair.Value) throw new IOException("Повреждён файл теста: " + pair.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)); File.Copy(source, dest, true);
            if (Hash(dest) != (string)pair.Value) throw new IOException("Ошибка копирования: " + pair.Key);
        }
        foreach (string name in new[] { "PawsReconnectTest.exe", "manifest.json", "LICENSE.Ensrick", "THIRD_PARTY.md", "START-HERE.txt", "VALIDATION.txt" })
            File.Copy(Path.Combine(Root, name), Path.Combine(target, name), true);
        CreateShortcut(target);
        File.WriteAllText(Path.Combine(Root, "prepared-" + Environment.MachineName + ".txt"), target + "\nVerified files: " + files.Count);
        return target;
    }
    static void CreateShortcut(string target)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
        object link = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell,
            new object[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Paw Reconnect Test.lnk") });
        var t = link.GetType();
        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { Path.Combine(target, "PawsReconnectTest.exe") });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { target });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(link);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }
    [STAThread] static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--prepare"))
        {
            try { var target = InstallPrivateCopy(delegate(string s) { }); File.WriteAllText(Path.Combine(Root, "prepared-" + Environment.MachineName + ".txt"), target); }
            catch (Exception e) { File.WriteAllText(Path.Combine(Root, "prepare-error-" + Environment.MachineName + ".txt"), e.ToString()); Environment.ExitCode = 1; }
            return;
        }
        Application.Run(new ReconnectTest());
    }
    ReconnectTest()
    {
        Text = "Paw’s Reconnect — локальный тест r2"; ClientSize = new Size(820, 530); MinimumSize = new Size(836, 569);
        StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(11, 25, 43); ForeColor = Color.White;
        Font = new Font("Segoe UI", 10); Padding = new Padding(16);
        mode.Items.AddRange(new object[] { "Vanilla", "Immortals 2.1" }); mode.SelectedIndex = 0;
        string choice = Path.Combine(Root, "selected-mode.txt"); if (File.Exists(choice) && File.ReadAllText(choice).Trim() == "immortals") mode.SelectedIndex = 1;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 86 };
        top.Controls.Add(new Label { Text = "Мод:", AutoSize = true, Padding = new Padding(0, 6, 8, 0) }); top.Controls.Add(mode);
        top.SetFlowBreak(mode, true); top.Controls.Add(autoVote);
        string voteChoice = Path.Combine(Root, "auto-vote.txt");
        autoVote.Checked = File.Exists(voteChoice) && File.ReadAllText(voteChoice).Trim() == "1";
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 94, WrapContents = true };
        launch.BackColor = Color.FromArgb(212, 175, 79); launch.ForeColor = Color.Black;
        if (SharedSource()) launch.Text = "Подготовить тест на этом ПК";
        drop.BackColor = Color.FromArgb(75, 43, 48);
        var saves = new Button { Text = "Сохранения теста", AutoSize = true };
        var logs = new Button { Text = "Журналы теста", AutoSize = true };
        foreach (Button b in new[] { launch, drop, saves, logs }) { b.FlatStyle = FlatStyle.Flat; b.Margin = new Padding(0, 0, 10, 10); actions.Controls.Add(b); }
        var help = new Label { Dock = DockStyle.Fill, Padding = new Padding(0, 9, 0, 0), Text =
            "На ПК и виртуалке выберите одинаковый мод и создайте сетевой матч.\n\n" +
            "При обрыве клиента хост сохранит матч и вернётся в лобби.\n" +
            "Запустите клиент повторно, подключитесь и нажмите Launch у хоста.\n" +
            "Кнопка «Имитировать обрыв» завершает только игру этого теста.\n\n" +
            "Обычный выход через меню тоже может вызвать восстановление.\n" +
            "После потери хоста новый хост сохраняет матч и оставляет паузу;\n" +
            "для возвращения прежнего хоста сохранение нужно загрузить вручную.\n\n" +
            "Игра и сохранения теста отдельные. Интерфейс игры — английский.\n" +
            "Это тест восстановления через лобби, без остальных функций Paw’s Patch." };
        Controls.Add(help); Controls.Add(top); Controls.Add(actions); Controls.Add(status);
        status.Text = "Готово. Steam должен быть запущен на обеих машинах.";
        launch.Click += async delegate { await Launch(); };
        drop.Click += delegate {
            if (game == null || game.HasExited) return;
            if (MessageBox.Show(this, "Оборвать соединение этой тестовой игры? Несохранённый локальный матч будет закрыт.", "Проверка восстановления", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            game.Kill(); simulatedDrop = true; status.Text = "Обрыв выполнен. Подождите возврата хоста в лобби и запустите игру снова.";
        };
        saves.Click += delegate { string path = ProfileData(); Directory.CreateDirectory(Path.Combine(path, "Save")); Process.Start("explorer.exe", "\"" + Path.Combine(path, "Save") + "\""); };
        logs.Click += delegate { Process.Start("explorer.exe", "\"" + Path.Combine(Root, "game", "Mods") + "\""); };
        poll.Tick += delegate { Poll(); }; poll.Start();
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) e.Cancel = true; };
    }
    string ProfileData()
    {
        string selected = currentProfile ?? (mode.SelectedIndex == 1 ? "Immortals" : "Vanilla");
        return Path.Combine(Root, "profiles", selected, "Kohan2", "data");
    }
    async Task Launch()
    {
        if (busy || (game != null && !game.HasExited)) return; busy = true; launch.Enabled = false; mode.Enabled = false; autoVote.Enabled = false;
        try
        {
            if (SharedSource())
            {
                var progress = new Progress<string>(s => status.Text = s);
                string target = await Task.Run(() => InstallPrivateCopy(s => ((IProgress<string>)progress).Report(s)));
                Process.Start(new ProcessStartInfo(Path.Combine(target, "PawsReconnectTest.exe")) { WorkingDirectory = target });
                busy = false; Close(); return;
            }
            status.Text = "Проверка файлов теста…";
            await Task.Run(delegate {
                if (!File.Exists(Path.Combine(Root, "reconnect-test.marker"))) throw new IOException("Не найдена метка отдельного теста.");
                var files = Files();
                foreach (string name in new[] { "game/k2.exe", "game/avifil32.dll", "game/shfolder.dll", "game/Mods/paws_reconnect.dll" })
                    if (Hash(SafePath(Root, name)) != (string)files[name]) throw new IOException("Файл отличается от тестовой сборки: " + name);
                if (Hash(Path.Combine(Root, "game", "k2.exe")) != GameHash) throw new IOException("Неизвестная версия игры.");
            });
            if (Process.GetProcessesByName("k2").Any()) throw new IOException("Сначала закройте уже запущенную игру на этой машине.");
            if (!Process.GetProcessesByName("steam").Any()) throw new IOException("Сначала запустите Steam и войдите в свой аккаунт.");
            currentProfile = mode.SelectedIndex == 1 ? "Immortals" : "Vanilla";
            string data = ProfileData(), dir = Path.Combine(Root, "game");
            Directory.CreateDirectory(Path.Combine(dir, "startup"));
            if (data.Length >= 130) throw new IOException("Путь слишком длинный. Переместите комплект, например, в D:\\PawsReconnectTest-r2.");
            Directory.CreateDirectory(Path.Combine(data, "User")); Directory.CreateDirectory(Path.Combine(data, "Save"));
            string vars = Path.Combine(data, "User", "UVars.tgi");
            if (!File.Exists(vars)) File.WriteAllText(vars, "[Vars]\n{\n int ResolutionX = 1280\n int ResolutionY = 720\n flag ResolutionWindowed = true\n flag ResolutionCoopFullscreenMode = false\n flag FirstRun = false\n flag ViewShowElapsedGameTime = true\n flag MouseClipCursor = false\n flag RunWhenInactive = true\n float FrameRateLimit = 60\n float AudioMainVolume = 0.3\n}\n", Encoding.Unicode);
            string depots = "adddepot data.rwd\nadddepot music.rwd\n";
            if (mode.SelectedIndex == 1) depots += "adddepot mod-immortals/\n";
            depots += "adddepot test-data/\nadddepot %USERDATA%/data/ 1\n";
            File.WriteAllText(Path.Combine(dir, "startup", "autoexec.txt"), depots, Encoding.ASCII);
            File.WriteAllText(Path.Combine(dir, "Mods", "plugins.txt"), "paws_reconnect.dll\n", Encoding.ASCII);
            File.WriteAllText(Path.Combine(Root, "selected-mode.txt"), mode.SelectedIndex == 1 ? "immortals" : "vanilla");
            string log = Path.Combine(dir, "Mods", "plugins.log");
            if (File.Exists(log)) { string archive = Path.Combine(Root, "logs"); Directory.CreateDirectory(archive); File.Copy(log, Path.Combine(archive, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-plugins.log")); }
            var start = new ProcessStartInfo(Path.Combine(dir, "k2.exe")) { WorkingDirectory = dir, UseShellExecute = false };
            start.EnvironmentVariables["SteamAppId"] = "97130"; start.EnvironmentVariables["SteamGameId"] = "97130";
            start.EnvironmentVariables["PAWS_RECOVERY_DATA"] = data;
            start.EnvironmentVariables["PAWS_RECOVERY_AUTO_VOTE"] = autoVote.Checked ? "1" : "0";
            File.WriteAllText(Path.Combine(Root, "auto-vote.txt"), autoVote.Checked ? "1" : "0");
            start.EnvironmentVariables["PAWS_TEST_DOCUMENTS"] = Path.GetDirectoryName(Path.GetDirectoryName(data));
            start.EnvironmentVariables.Remove("PAWS_TEST_JOIN");
            if (game != null) game.Dispose();
            game = Process.Start(start); exitReported = simulatedDrop = false;
            File.WriteAllText(Path.Combine(Root, "last-run.txt"), DateTime.Now.ToString("O") + "\nPID=" + game.Id + "\nMod=" + currentProfile + "\nData=" + data);
            status.Text = "Игра запускается. Ожидание подключения восстановления…"; drop.Enabled = true;
        }
        catch (Exception e) { status.Text = e.Message; MessageBox.Show(this, e.Message, "Тест не запущен", MessageBoxButtons.OK, MessageBoxIcon.Information); }
        finally { busy = false; if (game == null || game.HasExited) { launch.Enabled = true; mode.Enabled = true; autoVote.Enabled = true; } }
    }
    void Poll()
    {
        if (busy || game == null) return;
        if (game.HasExited) {
            drop.Enabled = false; launch.Enabled = true; mode.Enabled = true; autoVote.Enabled = true;
            if (!exitReported && !simulatedDrop) status.Text = game.ExitCode == 0 ? "Игра закрыта. Можно запустить снова." : "Игра завершилась с кодом " + game.ExitCode + ". Подробности в журналах теста.";
            exitReported = true; return;
        }
        try
        {
            string text; using (var f = new FileStream(Path.Combine(Root, "game", "Mods", "plugins.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var reader = new StreamReader(f)) text = reader.ReadToEnd();
            if (text.Contains("REFUSED:")) status.Text = "Восстановление не подключилось. Не начинайте матч; откройте журналы теста.";
            else if (text.LastIndexOf("RESUME_READY:", StringComparison.Ordinal) > text.LastIndexOf("MATCH_ROLE:", StringComparison.Ordinal)) status.Text = "Сохранение готово в лобби. Игрок может вернуться; после его готовности нажмите Launch.";
            else if (text.LastIndexOf("HOST_LOST:", StringComparison.Ordinal) > text.LastIndexOf("MATCH_ROLE:", StringComparison.Ordinal)) status.Text = "Хост потерян. Матч сохранён на новом хосте; загрузите сохранение через лобби.";
            else if (text.Contains("READY r2:")) status.Text = "Восстановление подключено · " + currentProfile + " · тест r2 · PID " + game.Id;
        }
        catch (IOException) { }
    }
}
