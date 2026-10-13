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
        _instance = new System.Threading.Mutex(true, "Local\\PawsPatchLauncher-Reliability" + (ActivityStore.IsSmokeTest || ActivityStore.LocalTestProfile is not null ? "-test-" + Environment.ProcessId : ""), out var first);
        if (!first) { Shutdown(); return; }
        var previous = ActivityStore.Read("launcher-run");
        PreviousUncleanExit = previous is { CleanExit: false } && !ActivityStore.IsAlive(previous);
        if (previous is not null) ActivityStore.Save("previous-launcher-run", previous);
        ActionJournal.Record("launcher.start");
        _run = ActivityStore.ForProcess(System.Diagnostics.Process.GetCurrentProcess());
        ActivityStore.Save("launcher-run", _run);
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
        if (!ActivityStore.IsCommunityTest && !ActivityStore.IsCustomModsTest && !e.Args.Contains("--skip-startup-update") && !e.Args.Contains("--update-health"))
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

    protected override void OnExit(ExitEventArgs e)
    {
        if (_run is not null) { _run.CleanExit = true; ActivityStore.Save("launcher-run", _run); }
        _instance?.Dispose();
        ActionJournal.Record("launcher.exit");
        ActionJournal.FlushAsync().Wait(TimeSpan.FromSeconds(2));
        base.OnExit(e);
    }
}
