using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private readonly DispatcherTimer _gameStatusTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void InitializeGameLaunchState()
    {
        _gameStatusTimer.Tick += (_, _) => RefreshGameLaunchState();
        Loaded += (_, _) => { if (!ActivityStore.IsSmokeTest) { RefreshGameLaunchState(); _gameStatusTimer.Start(); } };
        Activated += (_, _) => RefreshGameLaunchState();
        Closed += (_, _) => { _gameStatusTimer.Stop();_compatibilityClosed=true;_compatibilityCancellation?.Cancel(); };
    }

    private void RefreshGameLaunchState() => ApplyGameLaunchState(IsGameRunning());

    private void ApplyGameLaunchState(bool running)
    {
        RefreshGameSettingsState(running);
        RefreshCompatibility();
        LaunchButton.Content = running ? T("Игра запущена", "Game running") : LaunchButtonText;
        LaunchButton.IsEnabled = !running && !_busy && !_launchStarting && !FeedBlocksActions && !ArcaneAccessBlocked && !_settingsPending && _patchInstalled && _installationFailure is null && !_fileCheckFailed
            && (!CompatibilityRelevant || !CompatibilityBlocksLaunch) && _game is not null;
        AccountLogoutButton.IsEnabled = !_busy && !FeedBlocksActions && !_accountBusy;
        if (SocialDetailsOverlay.Visibility == System.Windows.Visibility.Visible) RefreshSocialCopyAvailability();
    }
}
