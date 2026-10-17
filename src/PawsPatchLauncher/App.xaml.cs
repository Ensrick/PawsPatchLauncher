using System.Windows;

namespace PawsPatchLauncher;

public partial class App : Application
{
    private void Tooltip_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement tooltip) { tooltip.UpdateLayout(); Motion.Reveal(tooltip); }
    }

    private System.Threading.Mutex? _instance;
    public static bool PreviousUncleanExit { get; private set; }
    private RunRecord? _run;
    public static bool StartupUpdateCompleted { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        if (ActivityStore.IsSmokeTest) TestProcessErrorMode.Enable();
        if (!ActivityStore.IsSmokeTest) ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            if (!await SelfUpdater.WaitForRestartHandoffAsync(e.Args)) { Shutdown(); return; }
        }
        catch (Exception error) { ActivityStore.Log(error); Shutdown(); return; }
        if (HeadlessMode.Active) { await RunHeadlessAsync(e); return; }
        if (!AcquireInstance()) { Shutdown(); return; }
        RecordStart();
        DispatcherUnhandledException += (_, args) =>
        {
            ActivityStore.Log(args.Exception);
            if (ActivityStore.IsSmokeTest)
            {
                Console.Error.WriteLine(args.Exception);
                // Do not show a modal error over the user's game or let a failed test pass.
                Environment.Exit(1);
                return;
            }
            MessageBox.Show(args.Exception.Message, "Paw's Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        ShellIcon.RefreshExecutable();
        base.OnStartup(e);
        if (ActivityStore.IsSmokeTest)
        {
            // PreviewRenderer supplies an inert StartupUri; executable smoke runs need the real window.
            if (StartupUri is null) { MainWindow = new MainWindow(); MainWindow.Show(); }
            return;
        }
        var configuration = SettingsStore.LoadConfiguration();
        var feed = new FeedClient(configuration);
        StartupWindow? startup = null;
        if (!ActivityStore.IsCustomModsTest && !e.Args.Contains("--skip-startup-update") && !e.Args.Contains("--update-health"))
        {
            startup = new StartupWindow(feed, new SettingsStore().Load().Language);
            MainWindow = startup;
            startup.Show();
            if (await startup.Completion) { Shutdown(); return; }
        }
        StartupUpdateCompleted = true;
        var window = new MainWindow(configuration, feed);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();
        startup?.Close();
    }

    private bool AcquireInstance()
    {
        _instance = new System.Threading.Mutex(true, "Local\\PawsPatchLauncher-Reliability" + (ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null ? "-test-" + Environment.ProcessId : ""), out var first);
        return first;
    }

    private void RecordStart(string detail = "")
    {
        var previous = ActivityStore.Read("launcher-run");
        PreviousUncleanExit = previous is { CleanExit: false } && !ActivityStore.IsAlive(previous);
        if (previous is not null) ActivityStore.Save("previous-launcher-run", previous);
        ActionJournal.Record("launcher.start", detail);
        _run = ActivityStore.ForProcess(System.Diagnostics.Process.GetCurrentProcess());
        ActivityStore.Save("launcher-run", _run);
    }

    // The launcher window, off-screen and hidden, driven by one command. No startup update check, so the
    // launcher never replaces itself in this mode; package downloads still use the signed feed.
    private async Task RunHeadlessAsync(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var output = HeadlessMode.OpenOutput();
        var args = HeadlessMode.Arguments(e.Args);
        if (args.Length == 0 || HeadlessMode.IsHelp(args[0]))
        {
            PawsPatchLauncher.MainWindow.WriteHeadlessHelp(output, new SettingsStore().Load().Language);
            output.Flush(); Shutdown(args.Length == 0 ? HeadlessMode.Usage : HeadlessMode.Success); return;
        }
        if (!AcquireInstance())
        {
            output.WriteLine("error: " + UiLanguages.Text(new SettingsStore().Load().Language,
                "Открыто окно лаунчера. Закройте его и повторите команду.", "A launcher window is open. Close it and run the command again."));
            output.Flush(); Shutdown(HeadlessMode.Busy); return;
        }
        RecordStart("headless");
        DispatcherUnhandledException += (_, args) =>
        {
            ActivityStore.Log(args.Exception);
            output.WriteLine("error: " + args.Exception.Message);
            output.Flush();
            args.Handled = true;
            Shutdown(HeadlessMode.Failure);
        };
        base.OnStartup(e);
        StartupUpdateCompleted = true;
        var configuration = SettingsStore.LoadConfiguration();
        // A test profile keeps its downloads too, so trying commands never touches the player's own launcher data.
        if (ActivityStore.LocalTestProfile is not null && string.IsNullOrWhiteSpace(configuration.CacheRoot))
            configuration.CacheRoot = Path.Combine(ActivityStore.Root, "cache");
        var window = new MainWindow(configuration, new FeedClient(configuration))
        {
            Left = -32000, Top = -32000, Width = 1050, Height = 680,
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual
        };
        MainWindow = window;
        window.Show();
        var code = await window.RunHeadlessCommandAsync(args, output);
        window.Close();
        Shutdown(code);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_run is not null) { _run.CleanExit = true; ActivityStore.Save("launcher-run", _run); }
        _instance?.Dispose();
        // Headless help and refused commands never started a launcher session; they leave the journal alone.
        if (HeadlessMode.Active && _run is null) { base.OnExit(e); return; }
        ActionJournal.Record("launcher.exit");
        ActionJournal.FlushAsync().Wait(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
