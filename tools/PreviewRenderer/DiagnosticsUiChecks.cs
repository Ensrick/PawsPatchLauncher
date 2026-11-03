using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class DiagnosticsUiChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Diagnostics UI checks require --smoke-test.");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(MainWindow w, string name, params object?[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, args);
        T Field<T>(MainWindow w, string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(w)!;
        void Set(MainWindow w, string name, object value) => typeof(MainWindow).GetField(name, flags)!.SetValue(w, value);
        Task Refresh(MainWindow w) => (Task)Invoke(w, "RefreshDiagnosticsArchiveAsync")!;
        Button Button(MainWindow w) => (Button)w.FindName("ShowDiagnosticsArchiveButton");
        MainWindow MakeWindow()
        {
            var w = new MainWindow { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, Width = 1050, Height = 680 };
            Field<PawsPatchLauncher.Localization>(w, "_text").SetLanguage(language);
            Invoke(w, "ApplyLanguage"); Invoke(w, "SetActivePage", "settings"); w.Show(); return w;
        }
        var first = MakeWindow(); MainWindow? restarted = null;
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
        async Task Scenario()
        {
            await Refresh(first);
            Set(first, "_incident", "Synthetic game crash");
            foreach (var page in new[] { "home", "modules", "friends", "about", "settings" })
            {
                Invoke(first, "SetActivePage", page);
                Invoke(first, "RefreshReliabilityVisibility");
                var incident = Field<Border>(first, "_incidentCard");
                Check(incident.Parent == first.FindName("RecoveryHost") &&
                    incident.Visibility == (page == "settings" ? Visibility.Visible : Visibility.Collapsed),
                    "Crash recovery escaped Settings on " + page);
            }
            Check(!Button(first).IsEnabled, "Show archive is enabled before creation.");
            var path = CreateFixture();
            Check(await (Task<bool>)Invoke(first, "RememberCreatedDiagnosticsAsync", path)!, "Completed archive was not saved.");
            Check(Button(first).IsEnabled && Field<DiagnosticArchiveReference>(first, "_lastDiagnosticArchive").Path == path, "Show archive was not enabled after completion.");
            var label = (TextBlock)first.FindName("DiagnosticsArchiveInfoText");
            Check(label.Text.Contains(Path.GetFileName(path)) && (string)label.ToolTip == path, "Archive filename/path missing.");
            string? shown = null;
            Set(first, "_revealDiagnosticArchive", (Func<string, Task>)(p => { shown = p; return Task.CompletedTask; }));
            Invoke(first, "ShowWorking", (Func<string>)(() => "ongoing install"));
            await (Task)Invoke(first, "ShowLastDiagnosticsArchiveAsync")!;
            Check(shown == path && Field<OperationFeedback>(first, "_feedback").Working, "Reveal chose wrong file or changed installation status.");
            first.Close(); restarted = MakeWindow(); await Refresh(restarted);
            Check(Button(restarted).IsEnabled && Field<DiagnosticArchiveReference>(restarted, "_lastDiagnosticArchive").Path == path, "New launcher instance lost last archive.");
            var next = CreateFixture("Latest, новый.zip");
            await (Task<bool>)Invoke(restarted, "RememberCreatedDiagnosticsAsync", next)!;
            Check(Field<DiagnosticArchiveReference>(restarted, "_lastDiagnosticArchive").Path == next, "New archive did not become latest.");
            Set(restarted, "_revealDiagnosticArchive", (Func<string, Task>)(p => { shown = p; return Task.CompletedTask; }));
            File.Delete(next); shown = null;
            await (Task)Invoke(restarted, "ShowLastDiagnosticsArchiveAsync")!;
            Check(!Button(restarted).IsEnabled && shown is null && Field<OperationFeedback>(restarted, "_toast").Failed, "Missing archive reached Explorer or remained enabled.");
            using (var zip = ZipFile.Open(next, ZipArchiveMode.Create)) zip.CreateEntry("restored.txt");
            await Refresh(restarted); Check(Button(restarted).IsEnabled, "Restored archive did not re-enable the button.");
            Set(restarted, "_revealDiagnosticArchive", (Func<string, Task>)(_ => throw new IOException("Synthetic shell error")));
            await (Task)Invoke(restarted, "ShowLastDiagnosticsArchiveAsync")!;
            Check(Button(restarted).IsEnabled && Field<OperationFeedback>(restarted, "_toast").Failed, "Shell failure was not handled / retry blocked.");
            var operation = ((TextBlock)restarted.FindName("OperationText")).Text;
            Set(restarted, "_revealDiagnosticArchive", (Func<string, Task>)(async _ => await Task.Delay(60)));
            var revealing = (Task)Invoke(restarted, "ShowLastDiagnosticsArchiveAsync")!;
            Check(!Button(restarted).IsEnabled, "Repeated reveal clicks are not guarded.");
            await revealing; Check(Button(restarted).IsEnabled && ((TextBlock)restarted.FindName("OperationText")).Text == operation, "Reveal did not restore button / changed operation status.");
            var refresh = Refresh(restarted); restarted.Close(); await refresh;
            Check(Field<bool>(restarted, "_diagnosticsClosed"), "Closing diagnostics left live UI work.");
            Console.WriteLine($"DIAGNOSTICS UI PASS {checks} {language}: creation, persisted reload, latest file, deletion/restoration, shell error, repeat click, status isolation; Explorer mocked");
        }
        try
        {
            var task = first.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += (_, _) => frame.Continue = false;
            task.ContinueWith(_ => first.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            timer.Start(); try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            if (!task.IsCompleted) throw new TimeoutException("Diagnostics UI checks did not complete.");
            task.GetAwaiter().GetResult();
        }
        finally { first.Close(); restarted?.Close(); }
    }

    internal static string CreateFixture(string name = "PawsPatch_Diagnostics_2026-09-06_163000.zip")
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Synthetic diagnostics require --smoke-test.");
        var directory = Path.Combine(ActivityStore.Root, "test-archives", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) zip.CreateEntry("synthetic-fixture.txt");
        return path;
    }
}
