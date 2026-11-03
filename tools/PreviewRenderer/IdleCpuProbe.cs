using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

// Diagnostic measurement, not a CPU assertion: concurrent apps and rendering affect results.
internal static class IdleCpuProbe
{
    internal static void Run(string output)
    {
        if (ActivityStore.LocalTestProfile is null || ActivityStore.IsSmokeTest)
            throw new InvalidOperationException("Use a separate --test-profile without smoke mode so normal timers run.");
        new SettingsStore().Save(new UserSettings { Language = "ru", ModNoticeSeen = true });
        var w = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null)
        {
            Left = -32000, Top = -32000, Width = 1440, Height = 900,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var results = new List<object>();
        async Task Scenario()
        {
            await Task.Delay(3000);
            foreach (var page in new[] { "home", "modules", "custom-mods", "friends", "settings", "about" })
            {
                typeof(MainWindow).GetMethod("SetActivePage", flags)!.Invoke(w, [page]);
                w.UpdateLayout();
                await Task.Delay(1500);
                using var process = Process.GetCurrentProcess();
                var cpu = process.TotalProcessorTime;
                var watch = Stopwatch.StartNew();
                double maxDelay = 0;
                for (var i = 0; i < 40; i++)
                {
                    var tick = Stopwatch.StartNew();
                    await Task.Delay(250);
                    maxDelay = Math.Max(maxDelay, tick.Elapsed.TotalMilliseconds - 250);
                }
                process.Refresh();
                var seconds = watch.Elapsed.TotalSeconds;
                var cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds;
                var percent = cpuSeconds / seconds * 100;
                results.Add(new { page, seconds, cpuSeconds, percentOfOneCore = percent, maxDispatcherDelayMs = maxDelay });
                Console.WriteLine($"IDLE CPU {page}: {percent:F2}% of one core; {seconds:F1}s; max dispatcher delay={maxDelay:F1}ms");
            }
            File.WriteAllText(Path.ChangeExtension(output, ".json"), JsonSerializer.Serialize(new
            {
                scope = "Offscreen WPF window, guest, normal timers, empty feed endpoints, no installation/download, no authenticated avatars. Does not cover visible GPU composition.",
                results
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        try
        {
            w.Show();
            var task = w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var frame = new DispatcherFrame();
            task.ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
            Dispatcher.PushFrame(frame);
            task.GetAwaiter().GetResult();
        }
        finally { w.Close(); }
    }
}
