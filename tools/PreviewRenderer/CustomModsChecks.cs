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
        var w = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null)
        { Left = -32000, Top = -32000, Width = 1050, Height = 680, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(w)!;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(w, value);
        T C<T>(string name) => (T)w.FindName(name);
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Custom mods UI: " + why); checks++; }
        void Open() { Set("_settingsPending", false); Call("OpenGameSettings"); C<RadioButton>("CustomModsTab").IsChecked = true; w.UpdateLayout(); }
        void Click(string name) => C<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        async Task Idle()
        {
            for (int i = 0; i < 200 && Field<bool>("_customModsBusy"); i++) await Task.Delay(20);
            Check(!Field<bool>("_customModsBusy"), "async operation completed");
            w.UpdateLayout();
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
            Check(C<Grid>("CustomModsPanel").IsVisible && !C<ScrollViewer>("GameSettingsScroll").IsVisible, "tab switches body");
            Check(!C<Button>("GameSettingsSave").IsEnabled && C<Button>("CustomModsAdd").IsEnabled, "empty state");
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
            Check(!Field<bool>("_customModsDirty") && C<Button>("CustomModsAdd").IsEnabled && !C<Button>("GameSettingsSave").IsEnabled,
                "cancel restores controls without dirtying draft");
            Check(Field<string?>("_customModsError") is null && Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft").Count == 0,
                "cancel has no error or imported mod");
            Check(File.ReadAllText(Path.Combine(root, "startup", "autoexec.txt")) == originalState && !Directory.Exists(Path.Combine(root, ".pawpatch", "custom-rwd")),
                "cancel does not change game files or cache archives");
            Set("_chooseRwdMod", (Func<string?>)(() => throw new IOException("Simulated picker failure")));
            Click("CustomModsAdd"); await Idle();
            Check(Field<string?>("_customModsError") is not null && C<Button>("CustomModsAdd").IsEnabled && !Field<bool>("_customModsDirty"),
                "picker failure is inline and allows retry");
            Set("_chooseRwdMod", (Func<string?>)(() => null));
            Click("CustomModsAdd"); await Idle();
            Check(Field<string?>("_customModsError") is null, "cancel after picker failure clears error");
            Set("_chooseRwdMod", (Func<string?>)(() => source));
            Click("CustomModsAdd"); await Idle();
            Check(Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla].Count == 1, "import appears");
            Check(!Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla][0].Enabled, "initially disabled");
            Click("CustomModsAdd"); await Idle();
            Check(Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla].Count == 1, "duplicate not added");
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Check(C<Button>("GameSettingsSave").IsEnabled, "changes enable save");
            running = true; Call("RefreshGameSettingsState", true);
            Check(!C<Button>("GameSettingsSave").IsEnabled && !C<Button>("CustomModsAdd").IsEnabled && !C<StackPanel>("CustomModsBody").IsEnabled, "running disables mutations");
            running = false; Call("RefreshGameSettingsState", false);
            w.UpdateLayout();
            var view = C<Border>("GameSettingsCard");
            Check(view.ActualWidth <= 1000 && view.ActualHeight <= 632, "fits small window");
            var draw = new DrawingVisual(); using (var dc = draw.RenderOpen()) dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(draw);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "custom-mods-" + language + ".png"))) png.Save(file);
            Click("GameSettingsCancel");
            Check(!new ModuleInstaller(root).LoadState().CustomMods.ContainsKey(GameMod.Vanilla), "cancel discards draft");
            Open(); Click("CustomModsAdd"); await Idle();
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Click("GameSettingsSave"); await Idle();
            Check(new ModuleInstaller(root).LoadState().CustomMods[GameMod.Vanilla][0].Enabled, "save persists enabled mod");
            Check(!C<Border>("GameSettingsOverlay").IsVisible, "save closes modal");
            Open();
            Check(!C<Button>("GameSettingsSave").IsEnabled && Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla][0].Enabled, "reopen retains saved state");
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = false }));
            Click("GameSettingsSave"); await Idle();
            Check(!new ModuleInstaller(root).LoadState().Modules.ContainsKey(CustomRwdMods.ModuleId), "disable removes mounts");
            settings.Mod = GameMod.Immortals; Open();
            Check(!C<Button>("CustomModsAdd").IsEnabled, "unapplied different game gated");
            Click("GameSettingsCancel");
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
