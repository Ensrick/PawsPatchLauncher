using System.Diagnostics;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace PawsPatchLauncher;

public partial class StartupWindow : Window
{
    private readonly FeedClient _feed;
    private readonly string _language;
    private readonly CancellationTokenSource _cancel = new();
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _installing, _finished, _started, _downloading;
    private bool _checkingConnection, _connectionFailed;
    private readonly Stopwatch _connectionWatch = new();
    private readonly System.Windows.Threading.DispatcherTimer _connectionTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    private string T(string ru, string en) => UiLanguages.Text(_language, ru, en);
    private string TF(FormattableString ru, FormattableString en) => UiLanguages.Format(_language, ru, en);
    public Task<bool> Completion => _completion.Task;
    public StartupWindow(FeedClient feed, string language, WindowPlacementStore? placementStore = null)
    {
        InitializeComponent(); _feed = feed; _language = language;
        // Do not create the main window to place the update window: both use the same native policy.
        // Off-screen rendering fixtures must not read or change the user's monitor preferences.
        if (!ActivityStore.IsSmokeTest || placementStore is not null)
            WindowPlacementPersistence.PositionStartupWindow(this, placementStore ?? new WindowPlacementStore(ActivityStore.Root));
        VersionText.Text = T("Установленная версия · ", "Installed version · ") + SelfUpdater.CurrentVersion.ToString(3);
        ContinueButton.Content = T("Открыть лаунчер", "Open launcher");
        CancelUpdateButton.Content = T("Отменить", "Cancel");
        CancelUpdateButton.ToolTip = T("Отменить обновление и открыть установленную версию лаунчера.", "Cancel the update and open the installed launcher.");
        FooterText.Text = T("Обновление лаунчера", "Launcher update");
        StageText.Text = T("Проверяем обновления", "Checking for updates");
        DetailText.Text = T("Ищем последнюю версию лаунчера…", "Looking for the latest launcher version…");
        _connectionTimer.Tick += (_, _) => RefreshConnectionWait(_connectionWatch.Elapsed);
        Closed += (_, _) => _connectionTimer.Stop();
        ContentRendered += async (_, _) => { if (!_started) { _started = true; Motion.Reveal((FrameworkElement)Content); await RunAsync(); } };
        Closing += (_, e) => { if (!_finished) { e.Cancel = true; if (!_installing) _cancel.Cancel(); } };
    }
    private void Header_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (!_installing && ContinueButton.Visibility == Visibility.Visible
            && StartupUpdateCheck.CanOpenInstalled(_connectionWatch.Elapsed, _connectionFailed, _checkingConnection)) _cancel.Cancel();
    }
    private void CancelUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (!_downloading || _installing || _finished || _cancel.IsCancellationRequested) return;
        CancelUpdateButton.IsEnabled = false;
        StageText.Text = T("Отменяем обновление", "Cancelling update");
        _cancel.Cancel();
    }
    private void RefreshConnectionWait(TimeSpan elapsed)
    {
        var canContinue = !_finished && !_installing && StartupUpdateCheck.CanOpenInstalled(elapsed, _connectionFailed, _checkingConnection);
        ContinueButton.IsEnabled = canContinue;
        if (canContinue && ContinueButton.Visibility != Visibility.Visible) Motion.Reveal(ContinueButton);
        else if (!canContinue) { ContinueButton.BeginAnimation(OpacityProperty, null); ContinueButton.Visibility = Visibility.Hidden; }
    }
    private void FinishConnectionCheck()
    {
        _checkingConnection = false;
        _connectionTimer.Stop();
        RefreshConnectionWait(_connectionWatch.Elapsed);
    }
    private async Task RunAsync()
    {
        var replacement = false;
        try
        {
            _checkingConnection = true; _connectionFailed = false;
            _connectionWatch.Restart(); _connectionTimer.Start();
            var check = await StartupUpdateCheck.RunAsync(token => _feed.GetLauncherUpdateAsync(token, TimeSpan.FromSeconds(2)),
                new Progress<(int Attempt, TimeSpan Remaining)>(p =>
                {
                    if (_finished || !_checkingConnection || _cancel.IsCancellationRequested) return;
                    _connectionFailed = p.Attempt > 1;
                    RefreshConnectionWait(_connectionWatch.Elapsed);
                    DetailText.Text = p.Attempt == 1 ? T("Ищем последнюю версию лаунчера…", "Looking for the latest launcher version…")
                        : TF($"Сервер пока недоступен. Повторяем попытку · до {Math.Ceiling(p.Remaining.TotalSeconds)} с", $"Server unavailable. Retrying · up to {Math.Ceiling(p.Remaining.TotalSeconds)} s");
                }), _cancel.Token);
            FinishConnectionCheck();
            var release = check.Release;
            if (check.Error is not null) ActivityStore.Log(check.Error);
            if (release is null || !SelfUpdater.IsNewer(release.Version) || SelfUpdater.IsBlocked(release.Sha256)) return;
            StageText.Text = T("Загружаем обновление", "Downloading update");
            DetailText.Text = T("Новая версия · ", "New version · ") + release.Version;
            FooterText.Text = T("При отмене откроется текущая версия", "Cancel to open the installed version");
            _downloading = true;
            CancelUpdateButton.IsEnabled = true;
            Motion.Reveal(CancelUpdateButton);
            PercentText.Text = "0%";
            SizeText.Text = Bytes(0) + " / " + Bytes(release.Size);
            using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(_cancel.Token);
            downloadCancellation.CancelAfter(StartupUpdateCheck.OfflineBudget);
            var watch = Stopwatch.StartNew();
            long previous = 0, lastTick = 0, receivedAtLastReport = 0;
            var progress = new CoalescingProgress<(long Received, long? Total)>(p =>
            {
                if (_finished || _installing || _cancel.IsCancellationRequested) return;
                // A slow transfer may continue indefinitely while bytes arrive. A stalled
                // connection must fall back to the installed launcher without a long HTTP timeout.
                if (p.Received > receivedAtLastReport) downloadCancellation.CancelAfter(StartupUpdateCheck.OfflineBudget);
                receivedAtLastReport = p.Received;
                var elapsed = watch.ElapsedMilliseconds;
                if (elapsed - lastTick < 180 && p.Received != p.Total && previous > 0) return;
                var speed = Math.Max(0, p.Received - previous) / Math.Max((elapsed - lastTick) / 1000d, .1);
                previous = p.Received; lastTick = elapsed;
                var total = p.Total is > 0 ? p.Total.Value : release.Size;
                SizeText.Text = Bytes(p.Received) + " / " + Bytes(total);
                var percent = Math.Clamp(100d * p.Received / total, 0, 100);
                DownloadProgress.IsIndeterminate = false;
                var from = DownloadProgress.Value;
                DownloadProgress.BeginAnimation(RangeBase.ValueProperty, null); DownloadProgress.Value = percent;
                if (percent > from && SystemParameters.ClientAreaAnimation)
                    DownloadProgress.BeginAnimation(RangeBase.ValueProperty, new DoubleAnimation(from, percent, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                PercentText.Text = $"{percent:0}%";
                var remaining = speed > 1024 ? TimeSpan.FromSeconds(Math.Clamp((total - p.Received) / speed, 0, 86400)).ToString(@"mm\:ss") : "—";
                SpeedText.Text = Bytes((long)speed) + T("/с · осталось ", "/s · remaining ") + remaining;
            });
            var downloaded = await _feed.DownloadLauncherAsync(release, progress, downloadCancellation.Token);
            downloadCancellation.CancelAfter(Timeout.InfiniteTimeSpan);
            _cancel.Token.ThrowIfCancellationRequested();
            _downloading = false; _installing = true; ContinueButton.IsEnabled = false;
            CancelUpdateButton.IsEnabled = false;
            Motion.Collapse(CancelUpdateButton);
            FooterText.Text = T("Лаунчер откроется после установки", "The launcher will open after installation");
            StageText.Text = T("Устанавливаем обновление", "Installing update");
            DetailText.Text = T("Лаунчер откроется автоматически. Предыдущая версия сохранена для восстановления.", "The launcher will open automatically. The previous version is retained for recovery.");
            DownloadProgress.IsIndeterminate = false;
            DownloadProgress.BeginAnimation(RangeBase.ValueProperty, null); DownloadProgress.Value = 100;
            PercentText.Text = "100%"; SpeedText.Text = "";
            await Task.Run(() => SelfUpdater.ScheduleReplacement(downloaded, release.Sha256));
            replacement = true;
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
        catch (Exception error) { ActivityStore.Log(error); }
        finally
        {
            _finished = true; _downloading = false; FinishConnectionCheck();
            CancelUpdateButton.IsEnabled = false; Motion.Collapse(CancelUpdateButton);
            _completion.TrySetResult(replacement);
        }
    }
    private static string Bytes(long value) => value >= 1073741824 ? $"{value / 1073741824d:0.00} GB" : value >= 1048576 ? $"{value / 1048576d:0.0} MB" : $"{value / 1024d:0.0} KB";
}
