using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawsPatchLauncher;
using PawsPatchLauncher.Rwd;

namespace PreviewRenderer;

internal static class CustomModsChecks
{
    internal static void Run(string language, string output)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Isolated profile required");
        Directory.CreateDirectory(output);
        var settings = new UserSettings { Language = language, Mod = GameMod.Vanilla, ModNoticeSeen = true };
        new SettingsStore().Save(settings);
        var root = Path.Combine(ActivityStore.Root, "custom-ui"); Directory.CreateDirectory(Path.Combine(root, "startup"));
        File.WriteAllText(Path.Combine(root, "startup", "autoexec.txt"), "adddepot data.rwd\r\n");
        File.WriteAllText(Path.Combine(root, "k2.exe"), "fixture");
        new ModuleInstaller(root).ReconcileAsync(new Dictionary<string, InstalledModule>(), settings: settings).GetAwaiter().GetResult();
        var source = Path.Combine(root, "Example mod.rwd");
        RwdArchive.Write(source, new Dictionary<string, byte[]> { ["test.tgi"] = [1] });
        var second = Path.Combine(root, "Second mod.rwd");
        RwdArchive.Write(second, new Dictionary<string, byte[]> { ["test.tgi"] = [2], ["other.tgi"] = [3] });
        var w = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null)
        { Left = -32000, Top = -32000, Width = 1050, Height = 680, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(w)!;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(w, value);
        T C<T>(string name) => (T)w.FindName(name);
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Custom mods UI: " + why); checks++; }
        // Leaving and re-entering the page is how a player reopens it.
        void Open() { Set("_settingsPending", false); Call("SetActivePage", "home"); Call("SetActivePage", "modmanager"); w.UpdateLayout(); }
        void Click(string name) => C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        List<CustomRwdMod> Draft() => Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft").GetValueOrDefault(GameMod.Vanilla) ?? [];
        List<CustomRwdMod> Saved() => new ModuleInstaller(root).LoadState().CustomMods.GetValueOrDefault(GameMod.Vanilla) ?? [];
        async Task Idle()
        {
            for (int i = 0; i < 200 && Field<bool>("_customModsBusy"); i++) await Task.Delay(20);
            Check(!Field<bool>("_customModsBusy"), "async operation completed");
            w.UpdateLayout();
        }
        void Snapshot(string name, FrameworkElement? target = null)
        {
            w.UpdateLayout();
            var view = target ?? C<StackPanel>("ModManagerPanel");
            Check(view.ActualWidth > 0 && view.ActualWidth <= 1000, "page fits small window: " + name);
            var draw = new DrawingVisual(); using (var dc = draw.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(11, 26, 44)), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
                dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            }
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(draw);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, name + "-" + language + ".png"))) png.Save(file);
        }
        bool running = false;
        Set("_gameRunningProbe", (Func<bool>)(() => running));
        Set("_chooseRwdMod", (Func<string?>)(() => source));
        Set("_game", new GameInstallation(root, Path.Combine(root, "k2.exe"), "fixture", "stable"));
        Set("_settings", settings); Call("ApplyLanguage"); Call("SetActivePage", "modules");
        w.Show(); w.UpdateLayout();
        async Task Scenario()
        {
            await Task.Delay(100);
            Open();
            Check(C<StackPanel>("ModManagerPanel").IsVisible && C<Button>("ModManagerNav").IsVisible, "mod manager is a page of its own");
            Check(!C<Button>("ModManagerApply").IsEnabled && !C<Button>("ModManagerRevert").IsEnabled && C<Button>("CustomModsAdd").IsEnabled, "empty state");
            Check(C<StackPanel>("CustomModsBody").Children.Count == 2, "base game row and empty hint");
            // No account and no release list: Vanilla is active, Immortals waits for a release list, Arcane Wars is locked with its reason.
            var gameRows = Field<List<(string Mod, Button Button, bool Allowed)>>("_baseGameButtons");
            Check(C<StackPanel>("ModManagerGamesBody").Children.Count == 3 && gameRows.Count == 2 && gameRows.All(r => r.Mod != GameMod.Vanilla), "game card lists three games, active Vanilla without action");
            Check(gameRows.All(r => !r.Button.IsEnabled), "games without a release list or access cannot be installed");
            Check(gameRows.Single(r => r.Mod == GameMod.ArcaneWars).Button.ToolTip is string reason && reason == (string)typeof(MainWindow).GetProperty("ArcaneAccessReason", flags)!.GetValue(w)!,
                "locked Arcane Wars explains why");
            Call("OpenGameSettings"); w.UpdateLayout();
            Check(C<Border>("GameSettingsOverlay").IsVisible && w.FindName("CustomModsTab") is null, "game settings dialog keeps only game options");
            Click("GameSettingsCancel"); w.UpdateLayout();
            Call("SetActivePage", "modules"); w.UpdateLayout();
            Check(C<Grid>("ModChoiceGrid").Visibility == Visibility.Collapsed && C<Button>("ComponentsChangeGameButton").IsVisible,
                "components page names the game and links to the Mod manager instead of choosing it");
            Snapshot("components", C<Grid>("MainOptionsHost"));
            Open();
            // Exercise real resource decoding, including when LauncherTestName changes the assembly.
            var glyphText = string.Join(" ", ChatGlyphs.All.Select(g => g.Token));
            var message = new ChatMessageText { Text = glyphText };
            Check(message.Text == glyphText, "chat message renders and retains every glyph");
            foreach (var glyph in ChatGlyphs.All)
                Check(ChatGlyphs.CreateImage(glyph).Source is BitmapSource { PixelWidth: > 0, PixelHeight: > 0 }, "glyph decodes: " + glyph.Id);
            var originalState = File.ReadAllText(Path.Combine(root, "startup", "autoexec.txt"));
            int pickerCalls = 0;
            bool pickerGuarded = false;
            Set("_chooseRwdMod", (Func<string?>)(() =>
            {
                pickerCalls++;
                pickerGuarded = Field<bool>("_customModsBusy") && !C<Button>("CustomModsAdd").IsEnabled;
                Click("CustomModsAdd"); // Nested dispatch must not open a second picker.
                message.Text = glyphText; // Chat redraw when focus returns from a native dialog.
                return null;
            }));
            Click("CustomModsAdd"); await Idle();
            Check(pickerCalls == 1 && pickerGuarded, "picker is guarded against reentry");
            Check(!Field<bool>("_customModsDirty") && C<Button>("CustomModsAdd").IsEnabled && !C<Button>("ModManagerApply").IsEnabled,
                "cancel restores controls without dirtying draft");
            Check(Field<string?>("_customModsError") is null && Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft").Count == 0,
                "cancel has no error or imported mod");
            Check(File.ReadAllText(Path.Combine(root, "startup", "autoexec.txt")) == originalState && !Directory.Exists(Path.Combine(root, ".pawpatch", "custom-rwd")),
                "cancel does not change game files or cache archives");
            Set("_chooseRwdMod", (Func<string?>)(() => throw new IOException("Simulated picker failure")));
            Click("CustomModsAdd"); await Idle();
            Check(Field<string?>("_customModsError") is not null && C<Button>("CustomModsAdd").IsEnabled && !Field<bool>("_customModsDirty"),
                "picker failure is inline and allows retry");
            Check(C<TextBlock>("ModManagerStatusText").IsVisible, "error is shown on the page");
            Set("_chooseRwdMod", (Func<string?>)(() => null));
            Click("CustomModsAdd"); await Idle();
            Check(Field<string?>("_customModsError") is null, "cancel after picker failure clears error");
            Set("_chooseRwdMod", (Func<string?>)(() => source));
            Click("CustomModsAdd"); await Idle();
            Check(Draft().Count == 1, "import appears");
            Check(!Draft()[0].Enabled, "initially disabled");
            Click("CustomModsAdd"); await Idle();
            Check(Draft().Count == 1, "duplicate not added");
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Check(C<Button>("ModManagerApply").IsEnabled && C<Button>("ModManagerRevert").IsEnabled, "changes enable apply and undo");
            running = true; Call("RefreshGameSettingsState", true);
            Check(!C<Button>("ModManagerApply").IsEnabled && !C<Button>("CustomModsAdd").IsEnabled && !C<StackPanel>("CustomModsBody").IsEnabled, "running disables mutations");
            running = false; Call("RefreshGameSettingsState", false);
            Call("SetActivePage", "home"); Call("SetActivePage", "modmanager"); w.UpdateLayout();
            Check(Draft().Count == 1 && Draft()[0].Enabled && Field<bool>("_customModsDirty"), "unapplied changes survive leaving the page");
            Snapshot("custom-mods");
            Click("ModManagerRevert"); w.UpdateLayout();
            Check(Draft().Count == 0 && !Field<bool>("_customModsDirty") && Saved().Count == 0, "undo discards the draft");
            Click("CustomModsAdd"); await Idle();
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Click("ModManagerApply"); await Idle();
            Check(Saved().Count == 1 && Saved()[0].Enabled, "apply persists enabled mod");
            Check(C<StackPanel>("ModManagerPanel").IsVisible && !C<Button>("ModManagerApply").IsEnabled, "apply stays on the page");
            Open();
            Check(!C<Button>("ModManagerApply").IsEnabled && Draft()[0].Enabled, "reopen retains saved state");
            // A rebuilt archive with the same file name updates its entry in place.
            var firstHash = Draft()[0].Sha256;
            RwdArchive.Write(source, new Dictionary<string, byte[]> { ["test.tgi"] = [9], ["new.tgi"] = [8] });
            Click("CustomModsAdd"); await Idle();
            Check(Draft().Count == 1 && Draft()[0].Sha256 != firstHash && Draft()[0].Enabled, "same name replaces in place and keeps the switch");
            Check(Field<string?>("_customModsNotice") is not null && Field<bool>("_customModsDirty"), "replacement is announced and pending");
            Click("ModManagerRevert"); w.UpdateLayout();
            Check(Draft().Count == 1 && Draft()[0].Sha256 == firstHash, "undo restores the previous archive");
            Click("CustomModsAdd"); await Idle();
            Set("_chooseRwdMod", (Func<string?>)(() => second));
            Click("CustomModsAdd"); await Idle();
            Call("ChangeCustomMod", 1, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            var resources = Draft().Select(m => (IReadOnlySet<string>?)Field<Dictionary<string, HashSet<string>?>>("_customModResources")[m.Sha256]).ToList();
            var overrides = CustomRwdMods.Overrides(resources);
            Check(overrides[0].Count == 0 && overrides[1].Count == 1 && overrides[1][0] == (0, 1), "lower mod overrides the shared file");
            Check(C<StackPanel>("CustomModsBody").Children.Count == 3, "base game row and two mods");
            Snapshot("mod-manager-order");
            Call("MoveCustomMod", 1, -1);
            Check(Draft()[0].Name == "Second mod", "move changes load order");
            Click("ModManagerApply"); await Idle();
            Check(Saved().Select(m => m.Name).SequenceEqual(["Second mod", "Example mod"]) && Saved().All(m => m.Enabled), "order and replacement persist");
            Check(Saved()[1].Sha256 != firstHash, "applied archive is the rebuilt one");
            Open(); // The fixture has no feed, so status refresh marks settings pending; reopening clears it as before.
            foreach (var index in new[] { 0, 1 }) Call("ChangeCustomMod", index, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = false }));
            Click("ModManagerApply"); await Idle();
            Check(!new ModuleInstaller(root).LoadState().Modules.ContainsKey(CustomRwdMods.ModuleId), "disable removes mounts");
            settings.Mod = GameMod.Immortals; Open();
            Check(!C<Button>("CustomModsAdd").IsEnabled && C<Button>("ModManagerComponentsButton").IsVisible, "unapplied different game gated");
            Click("ModManagerComponentsButton"); w.UpdateLayout();
            Check(!C<StackPanel>("ModManagerPanel").IsVisible, "gate leads to Components");
            Console.WriteLine($"CUSTOM MODS UI PASS {checks}: {language}");
        }
        var task = w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap(); var frame = new DispatcherFrame();
        task.ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        deadline.Tick += (_, _) => { deadline.Stop(); frame.Continue = false; }; deadline.Start();
        Dispatcher.PushFrame(frame); deadline.Stop();
        try { if (!task.IsCompleted) throw new TimeoutException("Custom mod checks"); task.GetAwaiter().GetResult(); }
        finally { w.Close(); }
    }
}
