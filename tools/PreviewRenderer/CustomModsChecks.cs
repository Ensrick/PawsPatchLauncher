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
        void Open() { Set("_settingsPending", false); Call("SetActivePage", "modules"); Call("SetActivePage", "custom-mods"); w.UpdateLayout(); }
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
            Check(C<Grid>("CustomModsPanel").IsVisible && !C<Border>("GameSettingsOverlay").IsVisible, "dedicated page opens without settings modal");
            Check(!C<Button>("CustomModsSave").IsEnabled && C<Button>("CustomModsAdd").IsEnabled, "empty state");
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
                Call("SetActivePage", "settings");
                Check(Field<string>("_activePage") == "custom-mods", "picker prevents navigation reentry");
                pickerGuarded = Field<bool>("_customModsBusy") && !C<Button>("CustomModsAdd").IsEnabled;
                Click("CustomModsAdd"); // Nested dispatch must not open a second picker.
                message.Text = glyphText; // Chat redraw when focus returns from a native dialog.
                return null;
            }));
            Click("CustomModsAdd"); await Idle();
            Check(pickerCalls == 1 && pickerGuarded, "picker is guarded against reentry");
            Check(!Field<bool>("_customModsDirty") && C<Button>("CustomModsAdd").IsEnabled && !C<Button>("CustomModsSave").IsEnabled,
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
            Check(C<Button>("CustomModsSave").IsEnabled, "changes enable save");
            Call("SetActivePage", "home");
            Check(Field<string>("_activePage") == "custom-mods" && Field<bool>("_customModsDirty"), "navigation cannot discard pending edits");
            Click("CustomModsNav");
            Check(Field<bool>("_customModsDirty") && DraftForNavigation().Count == 1, "reselecting manager retains draft");
            List<CustomRwdMod> DraftForNavigation() => Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla];
            Check(!C<Button>("LaunchButton").IsEnabled, "unapplied draft cannot launch silently");
            Call("OpenGameSettings");
            Check(C<Border>("GameSettingsOverlay").IsVisible, "game settings remain separately available");
            Click("GameSettingsCancel");
            Check(Field<bool>("_customModsDirty") && DraftForNavigation().Count == 1, "closing settings preserves mod draft");
            running = true; Call("RefreshGameSettingsState", true);
            Check(!C<Button>("CustomModsSave").IsEnabled && !C<Button>("CustomModsAdd").IsEnabled && !C<StackPanel>("CustomModsBody").IsEnabled, "running disables mutations");
            running = false; Call("RefreshGameSettingsState", false);
            w.UpdateLayout();
            var view = C<Grid>("CustomModsPanel");
            Check(view.ActualWidth <= 1000 && view.ActualHeight <= 632, "fits small window");
            var draw = new DrawingVisual(); using (var dc = draw.RenderOpen()) dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(draw);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using (var file = File.Create(Path.Combine(output, "custom-mods-" + language + ".png"))) png.Save(file);
            Click("CustomModsUndo"); Call("SetActivePage", "modules");
            Check(!new ModuleInstaller(root).LoadState().CustomMods.ContainsKey(GameMod.Vanilla), "cancel discards draft");
            Open(); Click("CustomModsAdd"); await Idle();
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Click("CustomModsSave"); await Idle();
            Check(new ModuleInstaller(root).LoadState().CustomMods[GameMod.Vanilla][0].Enabled, "save persists enabled mod");
            Check(!C<Border>("GameSettingsOverlay").IsVisible, "apply does not open a settings modal");
            Open();
            Check(!C<Button>("CustomModsSave").IsEnabled && Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla][0].Enabled, "reopen retains saved state");
            var originalMod = new ModuleInstaller(root).LoadState().CustomMods[GameMod.Vanilla][0];
            List<CustomRwdMod> Draft() => Field<Dictionary<string, List<CustomRwdMod>>>("_customModsDraft")[GameMod.Vanilla];
            var second = Path.Combine(root, "Balance update.rwd");
            using (var archive = RwdArchive.Open(source))
                RwdArchive.Write(second, archive.Entries.ToDictionary(e => e.Name, e => new byte[] { 9, 8, 7 }));
            Set("_chooseRwdMod", (Func<string?>)(() => second));
            Click("CustomModsAdd"); await Idle();
            Call("ChangeCustomMod", 1, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            IEnumerable<DependencyObject> Tree(DependencyObject node)
            {
                yield return node;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    foreach (var child in Tree(VisualTreeHelper.GetChild(node, i))) yield return child;
            }
            bool HasOverlap() => Tree(C<StackPanel>("CustomModsBody")).OfType<TextBlock>().Any(t => t.ToolTip is string);
            Check(Draft().Count == 2 && HasOverlap(), "enabled archives show matching paths");
            Check(Tree(C<StackPanel>("CustomModsBody")).OfType<TextBlock>().Any(t => Equals(t.ToolTip, "test.tgi")), "overlap tooltip names the actual shared resource");
            Check(Tree(C<StackPanel>("CustomModsBody")).OfType<TextBlock>().Any(t => t.Text.StartsWith("2 · ")), "enabled order badges");
            Call("ChangeCustomMod", 1, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = false }));
            Check(!HasOverlap(), "disabled mod does not contribute overlaps");
            Call("ChangeCustomMod", 1, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = true }));
            Call("MoveCustomMod", 1, -1);
            Check(Draft()[0].Name == "Balance update" && HasOverlap(), "reordering updates overlap direction");
            w.UpdateLayout();
            var preview = C<Grid>("CustomModsPanel");
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen()) dc.DrawRectangle(new VisualBrush(preview), null, new Rect(0, 0, preview.ActualWidth, preview.ActualHeight));
            var rendered = new RenderTargetBitmap((int)Math.Ceiling(preview.ActualWidth), (int)Math.Ceiling(preview.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            rendered.Render(drawing);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(rendered));
            using (var file = File.Create(Path.Combine(output, "custom-mods-enhanced-" + language + ".png"))) encoder.Save(file);
            foreach (var (width, height) in new[] { (1050, 680), (1440, 900) })
            {
                w.Width = width; w.Height = height; w.UpdateLayout();
                var page = C<Grid>("CustomModsPanel");
                foreach (var action in new[] { "CustomModsAdd", "CustomModsUndo", "CustomModsSave" })
                {
                    var button = C<Button>(action);
                    var bounds = button.TransformToAncestor(page).TransformBounds(new Rect(button.RenderSize));
                    Check(bounds.Left >= 0 && bounds.Right <= page.ActualWidth + 1 && bounds.Bottom <= page.ActualHeight + 1,
                        $"{action} fits {width}x{height}");
                }
                var content = (FrameworkElement)w.Content;
                var full = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                full.Render(content);
                var fullEncoder = new PngBitmapEncoder(); fullEncoder.Frames.Add(BitmapFrame.Create(full));
                using var fullFile = File.Create(Path.Combine(output, $"manager-{language}-{width}.png")); fullEncoder.Save(fullFile);
            }
            w.Width = 1050; w.Height = 680; w.UpdateLayout();
            Click("CustomModsUndo");
            Check(Draft().SequenceEqual([originalMod]) && !Field<bool>("_customModsDirty") && !C<Button>("CustomModsUndo").IsEnabled,
                "undo restores saved list and switches without closing");
            Check(C<Grid>("CustomModsPanel").IsVisible, "undo leaves manager open");
            Set("_chooseRwdMod", (Func<string?>)(() => null));
            Tree(C<StackPanel>("CustomModsBody")).OfType<Button>().First(b => Equals(b.Content, "↻")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(Draft()[0] == originalMod && !Field<bool>("_customModsDirty"), "cancel replacement leaves original intact");
            var invalid = Path.Combine(root, "invalid.rwd"); File.WriteAllText(invalid, "not an archive");
            Set("_chooseRwdMod", (Func<string?>)(() => invalid));
            Tree(C<StackPanel>("CustomModsBody")).OfType<Button>().First(b => Equals(b.Content, "↻")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(Draft()[0] == originalMod && !Field<bool>("_customModsDirty"), "invalid replacement leaves original intact");
            // Same-name re-import updates in place and retains the enabled switch.
            File.Copy(second, source, true);
            Set("_chooseRwdMod", (Func<string?>)(() => source));
            Click("CustomModsAdd"); await Idle();
            Check(Draft().Count == 1 && Draft()[0].Enabled && Draft()[0].Sha256 != originalMod.Sha256, "same-name update preserves position and enabled state");
            Check(new ModuleInstaller(root).LoadState().CustomMods[GameMod.Vanilla][0] == originalMod, "replacement is staged until apply");
            Click("CustomModsUndo");
            Check(Draft()[0] == originalMod, "undo replacement recovers old archive identity");
            var atCapacity = new List<CustomRwdMod> { originalMod };
            var library = new CustomRwdMods(root);
            for (int i = 1; i < CustomRwdMods.MaximumMods; i++)
            {
                var filler = Path.Combine(root, "limit-" + i + ".rwd");
                RwdArchive.Write(filler, new Dictionary<string, byte[]> { ["limit/" + i + ".txt"] = [(byte)i] });
                atCapacity.Add(library.Import(filler));
            }
            Set("_customModsDraft", new Dictionary<string, List<CustomRwdMod>> { [GameMod.Vanilla] = atCapacity });
            Call("CustomModsChanged", false);
            Check(C<Button>("CustomModsAdd").IsEnabled, "replacement remains available at capacity");
            Set("_chooseRwdMod", (Func<string?>)(() => second));
            Click("CustomModsAdd"); await Idle();
            Check(Draft().Count == 32 && Field<string?>("_customModsError") is not null, "33rd archive is rejected inline");
            Tree(C<StackPanel>("CustomModsBody")).OfType<Button>().First(b => Equals(b.Content, "↻")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(Draft().Count == 32 && Draft()[0].Enabled && Draft()[0].Name == "Balance update", "replacement at capacity retains count and switch");
            Check(C<ScrollViewer>("CustomModsScroll").ScrollableHeight > 0 && C<Button>("CustomModsSave").IsVisible, "long list scrolls with accessible apply action");
            Click("CustomModsUndo");
            Check(Draft().SequenceEqual([originalMod]), "undo capacity draft restores saved profile");
            // Explicit replacement also accepts a differently named archive.
            Set("_chooseRwdMod", (Func<string?>)(() => second));
            Tree(C<StackPanel>("CustomModsBody")).OfType<Button>().First(b => Equals(b.Content, "↻")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Idle();
            Check(Draft().Count == 1 && Draft()[0].Name == "Balance update" && Draft()[0].Enabled, "explicit replacement preserves enabled state");
            var replacement = Draft()[0];
            Click("CustomModsSave"); await Idle(); Open();
            Check(Draft()[0] == replacement && new ModuleInstaller(root).LoadState().CustomMods[GameMod.Vanilla][0] == replacement,
                "applied replacement survives reopening");
            Call("ChangeCustomMod", 0, (Func<CustomRwdMod, CustomRwdMod>)(m => m with { Enabled = false }));
            Click("CustomModsSave"); await Idle();
            Check(!new ModuleInstaller(root).LoadState().Modules.ContainsKey(CustomRwdMods.ModuleId), "disable removes mounts");
            Call("SetActivePage", "modules"); settings.Mod = GameMod.Immortals; Open();
            Check(!C<Button>("CustomModsAdd").IsEnabled, "unapplied different game gated");
            Click("CustomModsUndo"); Call("SetActivePage", "modules");
            Set("_game", null!); Open();
            Check(!C<Button>("CustomModsAdd").IsEnabled && !C<Button>("CustomModsSave").IsEnabled,
                "missing installation cannot import or apply mods");
            Call("SetActivePage", "modules");
            Check(!C<Grid>("CustomModsPanel").IsVisible && C<Grid>("ActionBar").IsVisible,
                "leaving clean manager restores components actions");
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
