using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class GameSettingsChecks
{
    internal static void Run(string language, string output)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Isolated profile required");
        Directory.CreateDirectory(output);
        new SettingsStore().Save(new UserSettings { Language = language, ModNoticeSeen = true });
        var root = Path.Combine(ActivityStore.Root, "game-settings-fixture");
        Directory.CreateDirectory(root);
        var file = Path.Combine(root, "UVars.tgi");
        const string fixture = "[Vars]\r\n{\r\n int ResolutionX = 2560\r\n int ResolutionY = 1440\r\n float FrameRateLimit = 240.000000\r\n float AudioMainVolume = 0.309220\r\n float Audio2DVolume = 0.706383\r\n float Audio3DVolume = 0.706383\r\n float AudioSpeechVolume = 0.709575\r\n float AudioMusicVolume = 0.308511\r\n flag ViewShowElapsedGameTime = true\r\n flag MinimapColorByKingdom = true\r\n string HotkeyConfiguration = visual_dvorak_config\r\n}\r\n";
        File.WriteAllText(file, fixture);
        var w = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null)
        { Left = -32000, Top = -32000, Width = 1050, Height = 680, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        object? Call(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(w)!;
        void Set(string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(w, value);
        T C<T>(string name) => (T)w.FindName(name);
        T Input<T>(string key) where T : Control => (T)Field<Dictionary<string, Control>>("_gameSettingsInputs")[key];
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("Game settings UI: " + why); checks++; }
        void Save() => C<Button>("GameSettingsSave").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        void Open() { C<Button>("GameSettingsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); w.UpdateLayout(); }
        void Close() => C<Button>("GameSettingsCancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        IEnumerable<T> Visuals<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) yield return found;
                foreach (var next in Visuals<T>(child)) yield return next;
            }
        }
        void Capture(string name, bool cardOnly = false)
        {
            w.UpdateLayout();
            var view = cardOnly ? C<Border>("GameSettingsCard") : (FrameworkElement)w.Content;
            var draw = new DrawingVisual();
            using (var dc = draw.RenderOpen()) dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, view.ActualWidth, view.ActualHeight));
            var image = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            image.Render(draw); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
            using var stream = File.Create(Path.Combine(output, name + "-" + language + ".png")); png.Save(stream);
        }
        var running = false;
        Set("_gameRunningProbe", (Func<bool>)(() => running));
        Set("_gameUserSettingsPath", (Func<string>)(() => file));
        Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language); Call("ApplyLanguage"); Call("SetActivePage", "modules");
        w.Show(); w.UpdateLayout();
        async Task Scenario()
        {
            await Task.Delay(100);
            Check(C<Button>("GameSettingsButton").IsVisible && C<Button>("GameSettingsButton").IsEnabled, "gear available");
            foreach (var mod in new[] { GameMod.Vanilla, GameMod.Immortals })
            {
                Field<UserSettings>("_settings").Mod = mod; Call("RefreshModControls"); w.UpdateLayout();
                Check(C<Button>("GameSettingsButton").IsVisible && C<Button>("GameSettingsButton").IsEnabled, "gear in " + mod);
            }
            var gear = C<Button>("GameSettingsButton").TransformToAncestor((FrameworkElement)w.Content).Transform(new Point());
            Check(gear.X + C<Button>("GameSettingsButton").ActualWidth <= w.Width - 20, "gear fits narrow window");
            Check(C<Button>("GameSettingsButton").TransformToAncestor(C<Border>("PatchChannelCard")).Transform(new Point()).X
                > C<Button>("BetaDetailsButton").TransformToAncestor(C<Border>("PatchChannelCard")).Transform(new Point()).X, "gear right of channel");
            Capture("components-gear");
            Open();
            Check(C<Border>("GameSettingsOverlay").IsVisible && !C<Grid>("MainBody").IsEnabled, "modal open and background disabled");
            Check(!C<Button>("GameSettingsSave").IsEnabled, "open does not dirty settings");
            Check(Input<ComboBox>("DisplayModes").SelectedItem is GameResolution { Width: 2560, Height: 1440 }, "current resolution selected");
            Check(Input<ComboBox>("DisplayModes").Items.OfType<GameResolution>().SequenceEqual(GameResolution.WidescreenChoices), "full ordered preset list");
            Check(Input<ComboBox>("DisplayModes").Style == w.FindResource("ReleaseCombo"), "shared dropdown style");
            Check(Visuals<TextBlock>(Input<ComboBox>("DisplayModes")).Any(t => t.Text == "2560 × 1440 · QHD"), "selected size visibly rendered");
            Check(!Input<ComboBox>("DisplayModes").IsEditable && !Field<Dictionary<string, Control>>("_gameSettingsInputs").ContainsKey("ResolutionX"), "no manual resolution entry");
            Check(Input<TextBox>("FrameRateLimit").Text == "240", "fps read");
            Check(Math.Abs(Input<Slider>("AudioMainVolume").Value - 30.922) < .0001, "precise original volume");
            Capture("game-settings");
            Capture("game-settings-card", true);
            var modes = Input<ComboBox>("DisplayModes");
            modes.IsDropDownOpen = true; await Task.Delay(120); w.UpdateLayout();
            var popup = (Popup)modes.Template.FindName("PART_Popup", modes);
            Check(popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 100 }, "dropdown opens");
            Check(PopupScrollIsolation.GetEnabled(popup), "dropdown isolates scrolling from page");
            var popupContent = (FrameworkElement)popup.Child;
            Check(popupContent.ActualHeight <= modes.MaxDropDownHeight + 10, "dropdown height bounded: " + popupContent.ActualHeight);
            var listScroll = Visuals<ScrollViewer>(popupContent).Single();
            listScroll.ScrollToBottom(); await Task.Delay(50); w.UpdateLayout();
            Check(listScroll.ScrollableHeight > 0 && listScroll.VerticalOffset > 0, "list scrolls to 4K");
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.MouseWheelEvent };
            popupContent.RaiseEvent(wheel);
            Check(wheel.Handled, "wheel cannot reach page");
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(popupContent.ActualWidth), (int)Math.Ceiling(popupContent.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(popupContent); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.Combine(output, "resolution-dropdown-" + language + ".png"))) encoder.Save(stream);
            modes.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(modes), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
            Check(!modes.IsDropDownOpen && C<Border>("GameSettingsOverlay").IsVisible, "Escape closes dropdown only");
            modes.SelectedItem = GameResolution.WidescreenChoices.Last();
            Check(C<Button>("GameSettingsSave").IsEnabled, "4K can be selected");
            modes.SelectedItem = new GameResolution(2560, 1440);
            Check(!C<Button>("GameSettingsSave").IsEnabled, "reverting selection removes edit");
            Input<TextBox>("FrameRateLimit").Text = "0";
            Check(!C<Button>("GameSettingsSave").IsEnabled && C<TextBlock>("GameSettingsStatus").IsVisible, "invalid fps blocked");
            Input<TextBox>("FrameRateLimit").Text = "144";
            Input<Slider>("AudioMusicVolume").Value = 40;
            Close();
            Check(File.ReadAllText(file) == fixture && C<Grid>("MainBody").IsEnabled, "cancel retains entire file");
            Open();
            modes = Input<ComboBox>("DisplayModes");
            modes.SelectedItem = new GameResolution(1920, 1080);
            running = true; Call("RefreshGameSettingsState", true);
            Check(!C<Button>("GameSettingsSave").IsEnabled, "running blocks save");
            Save(); Check(File.ReadAllText(file) == fixture, "running writes nothing");
            running = false; Call("RefreshGameSettingsState", false);
            Check(C<Button>("GameSettingsSave").IsEnabled, "closing game enables pending changes");
            Save();
            Check(!C<Border>("GameSettingsOverlay").IsVisible, "save closes modal");
            Check(File.ReadAllText(file) == fixture.Replace("2560", "1920").Replace("1440", "1080"), "save only replaces resolution");
            Check(Directory.EnumerateFiles(Path.Combine(root, "PawsLauncherBackups")).Count() == 1, "backup exists");
            Open(); Input<TextBox>("FrameRateLimit").Text = "120";
            File.AppendAllText(file, ";; changed externally");
            Save();
            Check(C<Border>("GameSettingsOverlay").IsVisible && !C<Button>("GameSettingsSave").IsEnabled, "conflicting file retains modal/error");
            Check(File.ReadAllText(file).EndsWith(";; changed externally"), "external edit retained");
            Close(); File.WriteAllText(file, fixture.Replace("2560", "1280").Replace("1440", "1024"));
            Open();
            Check(Input<ComboBox>("DisplayModes").SelectedItem?.GetType().GetProperty("Label")?.GetValue(Input<ComboBox>("DisplayModes").SelectedItem) as string == "1280 × 1024", "nonstandard value retained");
            Check(!C<Button>("GameSettingsSave").IsEnabled, "nonstandard value not changed on open");
            Input<TextBox>("FrameRateLimit").Text = "60"; Save();
            Check(GameUserSettings.Load(file).Number("ResolutionY") == 1024, "unrelated edit preserves nonstandard resolution");
            File.Delete(file); Open();
            Check(Input<ComboBox>("DisplayModes").SelectedItem is not GameResolution && !C<Button>("GameSettingsSave").IsEnabled, "missing file no implicit default");
            Input<ComboBox>("DisplayModes").SelectedItem = new GameResolution(1280, 720); Save();
            Check(GameUserSettings.Load(file).Number("ResolutionX") == 1280 && GameUserSettings.Load(file).Value("AudioMainVolume") is null, "new file writes only explicit selection");
            File.WriteAllText(file, "bad file"); Open();
            Check(C<TextBlock>("GameSettingsStatus").IsVisible && !C<Button>("GameSettingsSave").IsEnabled, "bad file blocked");
            Close();
            File.WriteAllText(file, fixture); Open();
            Input<ComboBox>("DisplayModes").SelectedItem = new GameResolution(1920, 1080);
            File.WriteAllBytes(file, new byte[1024 * 1024 + 1]);
            Save();
            Check(C<TextBlock>("GameSettingsStatus").IsVisible && C<Border>("GameSettingsOverlay").IsVisible, "late oversized file shows error without crashing");
            Check(new FileInfo(file).Length == 1024 * 1024 + 1, "late oversized file untouched");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(w), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            C<Border>("GameSettingsOverlay").RaiseEvent(escape);
            Check(escape.Handled && !C<Border>("GameSettingsOverlay").IsVisible, "Escape cancels modal");
            Console.WriteLine($"GAME SETTINGS UI PASS {checks}: {language}, native dropdown, modal, validation, exact file edits, process/conflict guards");
        }
        var task = w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap(); var frame = new DispatcherFrame();
        task.ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        deadline.Tick += (_, _) => { deadline.Stop(); frame.Continue = false; }; deadline.Start();
        Dispatcher.PushFrame(frame); deadline.Stop();
        try { if (!task.IsCompleted) throw new TimeoutException("Game settings checks"); task.GetAwaiter().GetResult(); }
        finally { w.Close(); }
    }
}
