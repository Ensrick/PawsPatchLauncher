using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PawsPatchLauncher;
using PawsPatchLauncher.Rwd;

namespace PreviewRenderer;

// `--headless` commands against the real MainWindow, a signed local feed and a text-only fixture game in a disposable
// smoke profile. No network, no native window on screen; the game start is a stub that records the request.
internal static class HeadlessChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null)
            throw new InvalidOperationException("Headless checks require a disposable --smoke-test profile.");
        var ru = language == "ru";
        var root = Path.Combine(ActivityStore.Root, "headless-cli", Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var sources = Path.Combine(root, "sources");
        var mods = Path.Combine(root, "mods");
        Directory.CreateDirectory(Path.Combine(game, "startup")); Directory.CreateDirectory(sources); Directory.CreateDirectory(mods);
        File.WriteAllText(Path.Combine(game, "k2.exe"), "Non-executable isolated headless fixture; never launch.");
        File.WriteAllText(Path.Combine(game, "startup", "autoexec.txt"), "adddepot data.rwd\r\n");
        using var signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var network = new RejectNetwork();
        using var http = new HttpClient(network);
        var configuration = new LauncherConfiguration
        {
            FeedUrls = [Path.Combine(sources, "stable.json")], BetaFeedUrls = [], CacheRoot = Path.Combine(root, "cache"),
            PublicKeyPem = signing.ExportSubjectPublicKeyInfoPem(), PreferredGameExecutable = "k2.exe"
        };
        var count = 0;
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException("Headless commands: " + why); count++; }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        InstallState State() => new ModuleInstaller(game).LoadState();

        async Task<PackageRelease> Package(string id, string version, int priority, Dictionary<string, string> files, bool required = false)
        {
            var archive = Path.Combine(sources, id + "-" + version + ".zip");
            var manifest = new ModuleArchiveManifest { Id = id, Version = version };
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                foreach (var (name, body) in files)
                {
                    var bytes = Encoding.UTF8.GetBytes(body);
                    manifest.Files.Add(new() { Path = name, Size = bytes.Length, Sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
                    using var output = zip.CreateEntry("payload/" + name).Open(); output.Write(bytes);
                }
                using var writer = new StreamWriter(zip.CreateEntry("module.json").Open());
                writer.Write(JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.ModuleArchiveManifest));
            }
            return new() { Id = id, Version = version, Priority = priority, Required = required, ExecutableIndependent = true,
                Name = new() { Ru = id, En = id }, Size = new FileInfo(archive).Length, Sha256 = await CryptoAndIO.Sha256Async(archive), Urls = [archive] };
        }
        async Task WriteFeed(ChannelManifest channel)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(channel, LauncherJsonContext.Default.ChannelManifest);
            var signed = new SignedFeedEnvelope { KeyId = "disposable-headless-test", Payload = Convert.ToBase64String(payload),
                Signature = Convert.ToBase64String(signing.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)) };
            await File.WriteAllTextAsync(configuration.FeedUrls[0], JsonSerializer.Serialize(signed, LauncherJsonContext.Default.SignedFeedEnvelope));
        }
        string Rwd(string name, byte marker)
        {
            var path = Path.Combine(mods, name + ".rwd");
            RwdArchive.Write(path, new Dictionary<string, byte[]> { ["data/" + name.ToLowerInvariant().Replace(' ', '_') + ".tgi"] = [marker] });
            return path;
        }

        var running = false;
        var started = new List<ProcessStartInfo>();
        new SettingsStore().Save(new UserSettings { Language = language, Mod = GameMod.Vanilla, ModNoticeSeen = true, GamePath = game,
            Channel = "stable", PawPatchEnabled = false, RussianLocalization = false });
        var window = new MainWindow(configuration, new FeedClient(configuration, http))
        {
            Width = 1050, Height = 680, Left = -32000, Top = -32000,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        object? Field(string name) => typeof(MainWindow).GetField(name, flags)!.GetValue(window);
        UserSettings Settings() => (UserSettings)Field("_settings")!;
        async Task<(int Code, string Text)> Command(params string[] args)
        {
            var writer = new StringWriter();
            var code = await (Task<int>)typeof(MainWindow).GetMethod("RunHeadlessCommandAsync", flags)!.Invoke(window, [args.ToList(), writer])!;
            return (code, writer.ToString());
        }
        string Say(string russian, string english) => ru ? russian : english;

        async Task Scenario()
        {
            var exe = await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe"));
            var feed = new ChannelManifest { PublishedAt = "2026-10-17T10:00:00Z", Game = new() { K2ExeSha256 = [exe] } };
            feed.Packages.Add(await Package("arcane-wars", "0.82.0", 10, new() { ["data/shared.txt"] = "AW" }, true));
            feed.Packages.Add(await Package("startup-base", "1.0.0", 20, new() { ["startup/base.txt"] = "base" }, true));
            feed.Packages.Add(await Package("pawpatch-core", "1.0.0", 30, new() { ["data/shared.txt"] = "PAW" }, true));
            feed.Packages.Add(await Package("game-localization-en", "1.0.0", 90, new() { ["startup/language.txt"] = "en" }));
            feed.Packages.Add(await Package("vanilla-localization-ru", "1.0.0", 100, new() { ["startup/language.txt"] = "ru" }));
            feed.Packages.Add(await Package("localization-ru", "1.0.0", 110, new() { ["startup/language.txt"] = "ru" }));
            feed.Packages.Add(await Package("immortals", "2.1.0", 10, new() { ["data/shared.txt"] = "IMM", ["data/imm.txt"] = "Immortals" }));
            feed.Packages.Add(await Package("immortals-localization-ru", "1.0.0", 100, new() { ["startup/language.txt"] = "ru" }));
            await WriteFeed(feed);

            Set("_game", new GameInstallation(game, Path.Combine(game, "k2.exe"), "fixture", "stable"));
            Set("_gameRunningProbe", new Func<bool>(() => running));
            Set("_startProcess", new Func<ProcessStartInfo, Process?>(info => { started.Add(info); return Process.GetCurrentProcess(); }));
            typeof(MainWindow).GetMethod("ApplyLanguage", flags)!.Invoke(window, null);
            Check(await (Task<bool>)typeof(MainWindow).GetMethod("CheckFeedAsync", flags)!.Invoke(window, [false])!, "the signed local feed was not accepted");

            // Usage, help and refusals.
            var help = await Command("help");
            Check(help.Code == HeadlessMode.Success && help.Text.Contains("select-game vanilla|immortals") && help.Text.Contains("mods move")
                && help.Text.Contains(Say("Коды выхода", "Exit codes")), "help lists the commands in the UI language");
            Check((await Command()).Code == HeadlessMode.Usage, "no command is a usage error");
            var unknown = await Command("frobnicate");
            Check(unknown.Code == HeadlessMode.Usage && unknown.Text.StartsWith("error: "), "an unknown command is a usage error");
            Check((await Command("status", "extra")).Code == HeadlessMode.Usage, "status takes no arguments");
            Check((await Command("set", "colors")).Code == HeadlessMode.Usage && (await Command("set", "bogus=on")).Code == HeadlessMode.Usage
                && (await Command("set", "paw=maybe")).Code == HeadlessMode.Usage && (await Command("set", "spawn=x9")).Code == HeadlessMode.Usage,
                "malformed set pairs, unknown keys and invalid values are usage errors");
            Check((await Command("select-game", "arcane-wars")).Code == HeadlessMode.Usage, "select-game accepts only Vanilla and Immortals");

            // Fresh game: nothing installed.
            var status = await Command("status");
            Check(status.Code == 0 && status.Text.Contains("selected-game: vanilla") && status.Text.Contains("pending: yes")
                && status.Text.Contains("applied: " + Say("ничего", "nothing")) && status.Text.Contains("launch: blocked")
                && status.Text.Contains("game: " + game), "status of an unapplied game: " + status.Text);
            var early = await Command("mods", "add", Rwd("Example", 1));
            Check(early.Code == HeadlessMode.Failure && early.Text.Contains("--headless apply"), "mods are refused before a game is applied");

            // Install and apply Vanilla through the window's primary action.
            var apply = await Command("apply");
            Check(apply.Code == 0 && apply.Text.Contains("action: ") && State().AppliedSettings?.Mod == GameMod.Vanilla, "apply installs Vanilla: " + apply.Text);
            Check((bool)Field("_patchInstalled")! && !(bool)Field("_settingsPending")!, "Vanilla is installed and nothing is pending after apply");
            var again = await Command("apply");
            Check(again.Code == 0 && again.Text.Contains(Say("Применять нечего.", "Nothing to apply.")), "a second apply has nothing to do");

            // Local RWD mods: add, duplicate, same-name replacement, enable, order, remove.
            var example = Rwd("Example", 1);
            var added = await Command("mods", "add", example);
            Check(added.Code == 0 && added.Text.Contains("1. [off] Example") && State().CustomMods[GameMod.Vanilla].Count == 1
                && !State().Modules.ContainsKey(CustomRwdMods.ModuleId), "a new mod is listed, off and not mounted");
            var duplicate = await Command("mods", "add", example);
            Check(duplicate.Code == 0 && duplicate.Text.Contains(Say("Этот архив уже добавлен.", "This archive is already in the list."))
                && State().CustomMods[GameMod.Vanilla].Count == 1, "the same archive is not added twice");
            var enabled = await Command("mods", "enable", "example");
            Check(enabled.Code == 0 && State().CustomMods[GameMod.Vanilla][0].Enabled && State().Modules.ContainsKey(CustomRwdMods.ModuleId)
                && File.ReadAllText(Path.Combine(game, "startup", "autoexec.txt")).Contains("adddepot PawsCustom/"), "enable mounts the mod in the startup file");
            var firstHash = State().CustomMods[GameMod.Vanilla][0].Sha256;
            var replaced = await Command("mods", "add", Rwd("Example", 2));
            Check(replaced.Code == 0 && replaced.Text.Contains(Say("Заменён новым файлом: Example", "Replaced with the new file: Example"))
                && State().CustomMods[GameMod.Vanilla] is [{ Enabled: true } rebuilt] && rebuilt.Sha256 != firstHash, "a rebuilt same-name archive replaces its entry in place and stays on");
            var second = await Command("mods", "add", Rwd("Second", 3), "--enable");
            Check(second.Code == 0 && State().CustomMods[GameMod.Vanilla] is [{ Name: "Example" }, { Name: "Second", Enabled: true }], "--enable adds a mod switched on at the end");
            var moved = await Command("mods", "move", "second", "1");
            Check(moved.Code == 0 && State().CustomMods[GameMod.Vanilla] is [{ Name: "Second" }, { Name: "Example" }], "move changes the load order");
            Check((await Command("mods", "move", "second", "5")).Code == HeadlessMode.Usage, "a position outside the list is a usage error");
            var disabled = await Command("mods", "disable", "2");
            Check(disabled.Code == 0 && State().CustomMods[GameMod.Vanilla] is [{ Enabled: true }, { Name: "Example", Enabled: false }], "a mod can be addressed by its list number");
            var unchanged = await Command("mods", "disable", "Example");
            Check(unchanged.Code == 0 && unchanged.Text.Contains(Say("без изменений", "unchanged")), "disabling a disabled mod changes nothing");
            var removed = await Command("mods", "remove", "Second");
            Check(removed.Code == 0 && State().CustomMods[GameMod.Vanilla] is [{ Name: "Example" }] && !State().Modules.ContainsKey(CustomRwdMods.ModuleId),
                "remove drops the mod and its mount");
            Check((await Command("mods", "remove", "Nope")).Code == HeadlessMode.Failure, "an unknown mod name fails");
            var text = Path.Combine(mods, "notes.txt"); File.WriteAllText(text, "not an archive");
            Check((await Command("mods", "add", text)).Code == HeadlessMode.Failure, "a file that is not an RWD archive is refused");
            Check((await Command("mods", "add", Path.Combine(mods, "missing.rwd"))).Code == HeadlessMode.Failure, "a missing file is refused");
            Check((await Command("mods", "add")).Code == HeadlessMode.Usage && (await Command("mods", "enable", "x", "--enable")).Code == HeadlessMode.Usage
                && (await Command("mods", "list", "--game", "arcane-wars")).Code == HeadlessMode.Usage, "mods argument errors are usage errors");
            var stored = await Command("mods", "add", Rwd("Stored", 4), "--enable", "--game", "immortals");
            Check(stored.Code == 0 && stored.Text.Contains(Say("Сохранено для Immortals", "Saved for Immortals")) && State().CustomMods[GameMod.Immortals] is [{ Name: "Stored", Enabled: true }]
                && State().AppliedSettings?.Mod == GameMod.Vanilla && !State().Modules.ContainsKey(CustomRwdMods.ModuleId), "another game's list is stored without mounting it");
            var listed = await Command("mods", "--game", "immortals");
            Check(listed.Code == 0 && listed.Text.Contains("mods (immortals): 1") && listed.Text.Contains("1. [on] Stored"), "mods lists another game's mods");

            // Selection: offered components only, then apply.
            var colors = await Command("set", "colors=on");
            Check(colors.Code == HeadlessMode.Failure && colors.Text.Contains("colors: "), "a component the page does not offer is refused");
            var gameText = await Command("set", "text=ru");
            Check(gameText.Code == 0 && GameLanguages.Text(Settings()) == "ru" && gameText.Text.Contains("pending: " + Say("да", "yes")), "set changes the game language like the page");
            var blockedLaunch = await Command("launch");
            Check(blockedLaunch.Code == HeadlessMode.Failure && started.Count == 0, "launch refuses a selection that is not applied");
            var applyLanguage = await Command("apply");
            Check(applyLanguage.Code == 0 && File.ReadAllText(Path.Combine(game, "startup", "language.txt")) == "ru", "apply installs the selected language");
            Check((await Command("set", "text=en")).Code == 0 && (await Command("apply")).Code == 0
                && File.ReadAllText(Path.Combine(game, "startup", "language.txt")) == "en", "the language returns to English");
            var same = await Command("set", "channel=release");
            Check(same.Code == 0 && same.Text.Contains(Say("без изменений", "unchanged")), "an unchanged value is reported, not applied");

            // Switch to Immortals: its stored mod list is mounted once Immortals is applied.
            var select = await Command("select-game", "immortals");
            Check(select.Code == 0 && Settings().Mod == GameMod.Immortals && select.Text.Contains("--headless apply"), "select-game chooses Immortals and asks for apply");
            Check((await Command("select-game", "immortals")).Text.Contains(Say("без изменений", "unchanged")), "selecting the selected game is unchanged");
            var immortals = await Command("apply");
            Check(immortals.Code == 0 && State().AppliedSettings?.Mod == GameMod.Immortals && File.Exists(Path.Combine(game, "data", "imm.txt"))
                && State().Modules.ContainsKey(CustomRwdMods.ModuleId), "apply installs Immortals and mounts its stored mods: " + immortals.Text);
            var immortalsStatus = await Command("status");
            Check(immortalsStatus.Code == 0 && immortalsStatus.Text.Contains("applied: immortals") && immortalsStatus.Text.Contains("1. [on] Stored")
                && immortalsStatus.Text.Contains("launch: ready"), "status reports the applied Immortals and its mods");

            // A running game blocks every change, never status.
            running = true;
            Check((await Command("apply")).Code == HeadlessMode.Busy && (await Command("mods", "disable", "Stored")).Code == HeadlessMode.Busy
                && (await Command("set", "text=ru")).Code == HeadlessMode.Busy && (await Command("select-game", "vanilla")).Code == HeadlessMode.Busy
                && (await Command("launch")).Code == HeadlessMode.Busy, "changes and launch are refused while the game runs");
            Check((await Command("status")).Code == 0 && State().CustomMods[GameMod.Immortals][0].Enabled, "status still works and nothing changed");
            running = false;

            // Launch through the window's Play path; the stub records the start instead of running anything.
            var launch = await Command("launch");
            Check(launch.Code == 0 && launch.Text.Contains("launched: process") && started.Count == 1
                && string.Equals(Path.GetDirectoryName(started[0].FileName), game, StringComparison.OrdinalIgnoreCase), "launch starts the game executable: " + launch.Text);
            Check(ActivityStore.Read("game-run") is null, "a headless start leaves no unobserved game run behind");

            Check(Field("_compatibilityPopup") is null, "no compatibility popup was opened");
            Check(network.Attempts == 0, "no network request was made");
            Console.WriteLine($"HEADLESS COMMANDS PASS {count} {language}: help/usage/refusals, status, select-game, set (offered only), mods add/replace/enable/disable/move/remove per game, apply install+settings, running-game refusal, stub launch; signed local ZIPs; no network/window/game.");
        }

        window.Show();
        var task = window.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        deadline.Tick += (_, _) => { deadline.Stop(); frame.Continue = false; };
        deadline.Start();
        Dispatcher.PushFrame(frame); deadline.Stop();
        try { if (!task.IsCompleted) throw new TimeoutException("Headless checks"); task.GetAwaiter().GetResult(); }
        finally { window.Close(); }
    }

    private sealed class RejectNetwork : HttpMessageHandler
    {
        internal int Attempts;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Attempts);
            throw new InvalidOperationException("External network is forbidden in the headless fixture.");
        }
    }
}
