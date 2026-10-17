using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class UiRoundTwoChecks
{
    internal static void Run(string language, string outputDirectory)
    {
        if (!ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null)
            throw new InvalidOperationException("Round two UI checks require a disposable smoke profile.");
        Directory.CreateDirectory(outputDirectory);
        var settingsPath = Path.Combine(ActivityStore.Root, "settings.json");
        var saved = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        new SettingsStore().Save(new UserSettings { Mod = GameMod.Vanilla, PawPatchEnabled = false, Language = language,
            ModNoticeSeen = true, NotificationSoundEnabled = false });
        var window = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [], CacheRoot = Path.Combine(ActivityStore.Root, "ui-round-two-cache") }, null)
        {
            Left = -32000, Top = -32000, Width = 1050, Height = 680, ShowActivated = false,
            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string method, params object?[] args) => typeof(MainWindow).GetMethod(method, flags)!.Invoke(window, args);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        void Set(string name, object? value) => typeof(MainWindow).GetField(name, flags)!.SetValue(window, value);
        T Control<T>(string name) => (T)window.FindName(name);
        var checks = 0;
        void Check(bool ok, string reason) { if (!ok) throw new InvalidOperationException("Round two UI: " + reason); checks++; }
        async Task Layout()
        {
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
            window.UpdateLayout();
        }
        void Capture(FrameworkElement element, string name)
        {
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(outputDirectory, name + "-" + language + ".png")); encoder.Save(file);
        }
        IEnumerable<T> Descendants<T>(DependencyObject node) where T : DependencyObject
        {
            if (node is T found) yield return found;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(node, i))) yield return child;
        }
        async Task Scenario()
        {
            Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language); Call("ApplyLanguage");
            var panel = Control<Border>("OperationStatusPanel");
            var details = Control<TextBlock>("TransferText");
            var progress = Control<ProgressBar>("OperationProgress");
            var pause = Control<Button>("CancelDownloadButton");
            IProgress<(long Received, long? Total)> Begin(string label) => (IProgress<(long, long?)>)Call("TransferProgress", label)!;
            foreach (var size in new[] { (1050d, 680d), (1280d, 800d) })
            foreach (var page in new[] { "home", "modules" })
            {
                window.Width = size.Item1; window.Height = size.Item2; Call("SetActivePage", page);
                Call("SetBusy", true, "Preparing files"); await Layout();
                if (Control<Border>("OperationExpandedDetails").Visibility != Visibility.Visible)
                {
                    Control<Button>("OperationDetailsButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Task.Delay(260); await Layout();
                }
                await Task.Delay(280); await Layout();
                var height = panel.ActualHeight;
                Check(height >= 124 && height <= 152 && panel.IsVisible, $"Expanded operation details lost their fixed work slots: height={height}, visible={panel.IsVisible}, page={page}, expanded={Field<bool>("_operationDetailsExpanded")}.");
                void Stable(string phase) => Check(Math.Abs(panel.ActualHeight - height) < 1, "Panel height changed during " + phase + " at " + size + "/" + page);
                pause.Visibility = Visibility.Visible;
                var transfer = Begin("Arcane Wars · " + new string('W', 160)); await Layout(); Stable("download setup");
                Check(details.Visibility == Visibility.Visible && details.Text.Length > 0 && !details.Text.Contains("/s") && !details.Text.Contains("/с"),
                    "Transfer setup is blank or invents an initial speed.");
                transfer.Report((131072, null)); await Layout(); Stable("unknown-size sample");
                Check(progress.IsIndeterminate && details.Text.Contains("?"), "Unknown transfer size was invented.");
                transfer.Report((1048576, 1048576)); await Layout(); Stable("known-size completion");
                Check(!progress.IsIndeterminate && Field<double>("_progressTarget") == 100 && details.Text.Contains('\n'), "Measured progress/details were lost.");
                if (size.Item1 == 1050 && page == "modules")
                {
                    Call("ShowWorking", (Func<string>)(() => language == "ru" ? "Загрузка: Arcane Wars 0.82" : "Downloading: Arcane Wars 0.82"));
                    await Task.Delay(240); await Layout(); Capture(panel, "download-panel");
                }
                Call("FinishTransfer"); pause.Visibility = Visibility.Collapsed; await Layout(); Stable("verification");
                var verification = details.Text;
                Check(details.Visibility == Visibility.Visible && verification.Length > 0 && !verification.Contains("/s") && !verification.Contains("/с"),
                    "Verification is blank or keeps an obsolete download speed.");
                Check(progress.IsIndeterminate, "File verification retained a completed download bar.");
                transfer.Report((2097152, 2097152)); await Layout();
                Check(details.Text == verification && Field<double>("_progressTarget") == 100, "Queued finished transfer changed the verification phase.");
                Call("ShowWorking", (Func<string>)(() => language == "ru" ? "Применяю настройки…" : "Applying settings…")); await Layout(); Stable("apply");
                Check(Control<TextBlock>("OperationText").Opacity >= .85, "Phase caption faded to blank.");
                if (size.Item1 == 1050 && page == "modules") Capture(panel, "apply-panel");
                var old = Begin("old package"); old.Report((900, 1000));
                var current = Begin("new package"); current.Report((20, 1000)); await Layout(); Stable("next package");
                Check(Field<double>("_progressTarget") == 2 && Field<long?>("_transferReceived") == 20, "Old package sample leaked into the next transfer.");
                Call("FinishTransfer"); var received = details.Text; old.Report((1000, 1000)); current.Report((1000, 1000));
                await Layout(); Check(details.Text == received && Field<double>("_progressTarget") == 2, "Cancelled/finished transfer accepted late samples.");
                await Task.Delay(240); await Layout();
                Check(!details.HasAnimatedProperties && !Control<TextBlock>("OperationText").HasAnimatedProperties,
                    "Phase transition left an attached animation clock.");
                Call("SetBusy", false, null); await Layout();
                Check(Control<Panel>("OperationWorkDetails").Visibility == Visibility.Collapsed && details.Visibility == Visibility.Collapsed
                    && progress.Visibility == Visibility.Collapsed && !progress.HasAnimatedProperties,
                    "Completed operation retained a reserved work area or running progress animation.");
            }
            Console.WriteLine($"ROUND TWO UI PASS {checks} {language}: fixed operation geometry at two sizes/Home/footer, measured transfer data, verify/apply, stale callbacks, phase motion cleanup.");
        }
        try
        {
            Set("_gameRunningProbe", new Func<bool>(() => false));
            window.Show();
            var dispatcher = window.Dispatcher;
            var task = dispatcher.InvokeAsync(Scenario).Task.Unwrap(); var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(40) }; timeout.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timeout.Start(); try { Dispatcher.PushFrame(frame); } finally { timeout.Stop(); }
            if (!task.IsCompleted) throw new TimeoutException("Round two UI checks timed out."); task.GetAwaiter().GetResult();
        }
        finally
        {
            Set("_busy", false); window.Close();
            if (saved is null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, saved);
        }
    }
}
