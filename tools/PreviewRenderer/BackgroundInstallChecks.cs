using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class BackgroundInstallChecks
{
    sealed class DirectProgress(Action<(long Received, long? Total)> report) : IProgress<(long Received, long? Total)>
    { public void Report((long Received, long? Total) value) => report(value); }

    internal static void Run(string output)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Isolated profile required.");
        var root = Path.Combine(ActivityStore.Root, "background-install"); Directory.CreateDirectory(root);
        var w = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null)
        { Left = -30000, Top = -30000, Width = 1050, Height = 680, ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
        int uiThread = Environment.CurrentManagedThreadId, checks = 0, ticks = 0, callbacks = 0;
        double maxGap = 0;
        var watch = Stopwatch.StartNew(); double lastTick = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { var now = watch.Elapsed.TotalMilliseconds; if (lastTick > 0) maxGap = Math.Max(maxGap, now - lastTick); lastTick = now; ticks++; };
        void Check(bool value, string label) { if (!value) throw new Exception("Background install: " + label); checks++; }
        w.Show(); w.UpdateLayout();
        async Task Scenario()
        {
            // Prepare source data on a worker too; the dispatcher measures actual operations below.
            var package = await Task.Run(() =>
            {
                var manifest = new ModuleArchiveManifest { Id = "background-fixture", Version = "1.0.0" };
                var archive = Path.Combine(root, "source.zip");
                using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
                {
                    var content = new byte[32768]; new Random(20261026).NextBytes(content);
                    var hash = Convert.ToHexString(SHA256.HashData(content));
                    for (int i = 0; i < 256; i++)
                    {
                        string name = $"data/test-{i}.bin";
                        manifest.Files.Add(new ModuleFile { Path = name, Size = content.Length, Sha256 = hash });
                        using var stream = zip.CreateEntry("payload/" + name, CompressionLevel.Fastest).Open(); stream.Write(content);
                    }
                    using var writer = new StreamWriter(zip.CreateEntry("module.json").Open());
                    writer.Write(JsonSerializer.Serialize(manifest, LauncherJsonContext.Default.ModuleArchiveManifest));
                }
                return new PackageRelease { Id = manifest.Id, Version = manifest.Version, Size = new FileInfo(archive).Length,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))), Urls = [archive] };
            });
            timer.Start();
            var feed = new FeedClient(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [], CacheRoot = Path.Combine(root, "cache") });
            var threads = new System.Collections.Concurrent.ConcurrentBag<int>();
            var archive = await feed.DownloadVerifiedAsync(package, new DirectProgress(_ => threads.Add(Environment.CurrentManagedThreadId)));
            Check(threads.Count > 0 && threads.All(t => t != uiThread), "download/copy reports originate outside UI thread");
            Check(Environment.CurrentManagedThreadId == uiThread, "caller resumes on dispatcher");
            var installer = new ModuleInstaller(Path.Combine(root, "game"));
            var prepared = await installer.PrepareAsync(package, archive);
            Check(prepared.Files.Count == 256, "background extraction and per-file verification");
            int guardThread = 0;
            await installer.ReconcileAsync(new Dictionary<string, InstalledModule> { [package.Id] = prepared }, ensureGameClosed: () =>
            {
                guardThread = Environment.CurrentManagedThreadId;
                // Real commit guard blocks until the dispatcher answers. Running this
                // on the UI would deadlock/fail, rather than merely lose an animation.
                var callback = w.Dispatcher.InvokeAsync(() => callbacks++, DispatcherPriority.Input);
                if (!callback.Task.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException("UI unavailable during commit.");
            });
            Check(guardThread != uiThread && callbacks > 0, "commit stays off UI while UI responds");
            Check((await installer.VerifyAsync()).Count == 0, "installed payload remains valid");
            await installer.UninstallAsync();
            Check(!File.Exists(Path.Combine(root, "game", "data", "test-0.bin")), "uninstall restores originals");
            await new PatchRecovery(Path.Combine(root, "game")).RollbackAsync(installer.LoadState());
            Check(File.Exists(Path.Combine(root, "game", "data", "test-0.bin")), "background rollback restores installation");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            var canceledRoot = Path.Combine(root, "never-started");
            try { await new ModuleInstaller(canceledRoot).PrepareAsync(package, archive, canceled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(!Directory.Exists(canceledRoot), "pre-cancellation makes no filesystem changes"); }
            Check(ticks >= 3, "dispatcher kept processing input-priority timer");
            timer.Stop();

            int reports = 0, latest = -1;
            var progress = new CoalescingProgress<int>(n => { Check(Environment.CurrentManagedThreadId == uiThread, "progress returns to dispatcher"); reports++; latest = n; });
            for (int i = 0; i < 100000; i++) progress.Report(i);
            Check(reports == 0, "progress never invokes UI inline");
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Check(reports == 1 && latest == 99999, "100000 chunks become one pending UI callback with latest value");

            using var pickerCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            bool pickerOpened = false;
            var pickerTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            pickerTimer.Tick += (_, _) => { if (w.OwnedWindows.Count > 0) { pickerOpened = true; pickerCancellation.Cancel(); } };
            pickerTimer.Start();
            try
            {
                var method = typeof(MainWindow).GetMethod("ChooseModDbArchiveAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
                await Task.Run(() => (Task<string?>)method.Invoke(w, [pickerCancellation.Token])!);
                throw new Exception("Picker cancellation ignored");
            }
            catch (OperationCanceledException) { Check(pickerOpened, "ModDB interaction dispatches from worker to UI and cancels"); }
            finally { pickerTimer.Stop(); }
            var result = new { Checks = checks, DispatcherTicks = ticks, MaxTickGapMs = Math.Round(maxGap, 2), WorkerCommitUiCallbacks = callbacks, CoalescedReports = 100000 };
            File.WriteAllText(Path.Combine(output, "background-install.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"BACKGROUND INSTALL PASS {checks}: download, extraction, hashes, commit, uninstall/rollback, cancellation, ModDB UI bridge and progress coalescing; ticks={ticks}, max gap={maxGap:F1}ms.");
        }
        var task = w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap(); var frame = new DispatcherFrame();
        task.ContinueWith(_ => w.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
        var deadline = new DispatcherTimer { Interval = TimeSpan.FromSeconds(90) };
        deadline.Tick += (_, _) => { deadline.Stop(); frame.Continue = false; }; deadline.Start();
        Dispatcher.PushFrame(frame); deadline.Stop();
        try { if (!task.IsCompleted) throw new TimeoutException("Background install check"); task.GetAwaiter().GetResult(); }
        finally { timer.Stop(); w.Close(); }
    }
}
