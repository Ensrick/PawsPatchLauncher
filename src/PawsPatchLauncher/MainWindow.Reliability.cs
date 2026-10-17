using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private ChannelManifest? _latestChannel;
    private readonly List<(Action<string> Set, string Ru, string En)> _reliabilityLabels = [];
    private readonly List<Button> _reliabilityActions = [];
    private Border _recoveryCard = null!, _multiplayerCard = null!, _importCard = null!, _versionCard = null!, _incidentCard = null!;
    private Button _rollbackButton = null!, _workingButton = null!, _copyReadiness = null!;
    private TextBox _importInput = null!, _peerInput = null!;
    private TextBlock _readinessText = null!, _comparisonText = null!, _incidentText = null!, _releaseStatus = null!;
    private ComboBox _releaseChoice = null!;
    private bool _loadingVersions;
    private string? _incident;
    private string? _readinessIdentity;
    private string? _checkedConfiguration;
    private ReadinessReport? _readiness;
    private CancellationTokenSource? _downloadCancellation;
    private readonly DispatcherTimer _gameTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private RunRecord? _observedRun;
    private Process? _observedProcess;
    private bool _observing;
    private bool _workingSaved;
    private readonly HashSet<string> _loadedPrevious = [];

    private string T(string ru, string en) => UiLanguages.Text(_text.Language, ru, en);
    private string TF(FormattableString ru, FormattableString en) => UiLanguages.Format(_text.Language, ru, en);
    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / 1073741824d:0.00} GB"
        : bytes >= 1024 * 1024 ? $"{bytes / 1048576d:0.0} MB" : $"{bytes / 1024d:0.0} KB";

    private void InitializeReliabilityUi()
    {
        void Label(Action<string> set, string ru, string en) { _reliabilityLabels.Add((set, ru, en)); set(T(ru, en)); }
        TextBlock Text(StackPanel panel, string ru, string en, bool heading = false)
        {
            var item = heading ? new TextBlock { Style = (Style)FindResource("CardTitle") }
                : new TextBlock { Style = (Style)FindResource("CardDescription"), Margin = new Thickness(0, 0, 0, 10) };
            Label(s => item.Text = s, ru, en); panel.Children.Add(item); return item;
        }
        Button Button(Panel panel, string ru, string en, RoutedEventHandler action, IconKind icon = IconKind.None)
        {
            var item = new Button { Style = (Style)FindResource("GhostButton"), Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, panel is WrapPanel ? 8 : 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
            LauncherIcon.SetKind(item, icon);
            Label(s => item.Content = s, ru, en); item.Click += action; panel.Children.Add(item); _reliabilityActions.Add(item); return item;
        }
        TextBox Input(StackPanel panel)
        {
            var box = new TextBox { Style = (Style)FindResource("ConfigurationInput"), MaxLength = 256 };
            panel.Children.Add(box); return box;
        }
        StackPanel Card(out Border border)
        {
            var panel = new StackPanel();
            border = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 14), Child = panel, Visibility = Visibility.Collapsed };
            ReliabilityPanels.Children.Add(border); return panel;
        }

        var incident = Card(out _incidentCard);
        Text(incident, "Восстановление после ошибки", "Recovery after an error", true);
        _incidentText = Text(incident, "", "");
        Button(incident, "Создать архив диагностики", "Create diagnostic archive", DiagnosticsButton_Click, IconKind.Diagnostics);
        Button(incident, "Понятно", "Dismiss", (_, _) => { _incident = null; RefreshReliabilityVisibility(); });

        var recovery = Card(out _recoveryCard);
        ReliabilityPanels.Children.Remove(_recoveryCard);
        RecoveryHost.Children.Add(_recoveryCard);
        RegisterName("RecoveryCard", _recoveryCard);
        Text(recovery, "Восстановление", "Recovery", true);
        Text(recovery, "Предыдущая установка сохраняется вместе с изменёнными и удалёнными файлами. При откате выпуск закрепляется до вашего выбора обновиться.",
            "The previous installation retains replaced and removed files. Rolling back pins that release until you choose to update.");
        _rollbackButton = Button(recovery, "Вернуть предыдущую установку", "Restore previous installation", RollbackPatch_Click, IconKind.Undo);
        _workingButton = Button(recovery, "Восстановить рабочие настройки", "Restore working settings", RestoreWorking_Click, IconKind.Undo);
        Text(recovery, "Рабочие настройки: набор, с которым окно игры открылось и работало не менее 20 секунд. Это не проверка отсутствия рассинхронов.",
            "Working settings means the game window opened and stayed running for at least 20 seconds. This does not verify multiplayer synchronization.");
        Text(recovery, "Обновления лаунчера: предыдущий EXE сохраняется; если новая версия не подтвердит запуск окна за 60 секунд, помощник вернёт старую и заблокирует повтор этого обновления.",
            "Launcher updates retain the previous EXE. If the new version does not confirm its window within 60 seconds, the helper restores the old version and blocks another attempt with that update.");

        var import = Card(out _importCard);
        Text(import, "Импорт настроек друга", "Import a friend's settings", true);
        Text(import, "Вставьте код конфигурации друга. Настройки применятся к файлам при установке или запуске игры.",
            "Paste your friend's configuration code. Files are updated when you install or launch the game.");
        _importInput = Input(import);
        Label(s => System.Windows.Automation.AutomationProperties.SetName(_importInput, s), "Код конфигурации друга", "Friend's configuration code");
        var importActions = new WrapPanel(); import.Children.Add(importActions); RegisterName("ImportActions", importActions);
        Button(importActions, "Вставить из буфера", "Paste from clipboard", async (_, _) => await PasteTextAsync(_importInput), IconKind.Paste);
        Button(importActions, "Применить код", "Apply code", ImportConfiguration_Click, IconKind.Check);

        var multiplayer = Card(out _multiplayerCard);
        Text(multiplayer, "Готовность к мультиплееру", "Multiplayer readiness", true);
        Text(multiplayer, "Проверка читает установленные файлы и дополнительные игровые данные. Отправьте отпечаток другу, вставьте его ответ и сравните. В отпечаток входит и локализация: разные языки дадут различие, даже если они совместимы.",
            "Checks installed files and extra game data. Send the fingerprint to a friend, paste theirs and compare. Localization is included: different languages differ even if compatible.");
        Button(multiplayer, "Проверить установленную конфигурацию", "Check installed configuration", CheckReadiness_Click, IconKind.Shield);
        _readinessText = Text(multiplayer, "Ещё не проверено", "Not checked yet");
        _reliabilityLabels.RemoveAt(_reliabilityLabels.Count - 1);
        _copyReadiness = Button(multiplayer, "Скопировать отпечаток", "Copy fingerprint", CopyReadiness_Click, IconKind.Copy);
        _peerInput = Input(multiplayer);
        Label(s => System.Windows.Automation.AutomationProperties.SetName(_peerInput, s), "Отпечаток конфигурации друга", "Friend's configuration fingerprint");
        var peerActions = new WrapPanel(); multiplayer.Children.Add(peerActions); RegisterName("PeerActions", peerActions);
        Button(peerActions, "Вставить отпечаток друга", "Paste friend's fingerprint", async (_, _) => await PasteTextAsync(_peerInput), IconKind.Paste);
        Button(peerActions, "Сравнить с другом", "Compare with friend", CompareReadiness_Click, IconKind.Compare);
        _comparisonText = Text(multiplayer, "", "");

        var versions = Card(out _versionCard);
        Text(versions, "Версия патча", "Patch version", true);
        Text(versions, "Выберите «Последняя», чтобы получать обновления выбранного канала. Предыдущие Beta и сохранённые установки можно закрепить; лаунчер при этом продолжает обновляться.",
            "Latest follows the selected channel. Previous Beta releases and saved installations can be pinned while the launcher continues updating.");
        _releaseChoice = new ComboBox { Style = (Style)FindResource("ReleaseCombo"), Margin = new Thickness(0, 0, 0, 10), MaxDropDownHeight = 200, DisplayMemberPath = "Label",
            ItemsSource = new[] { new ReleaseChoice(T("Последняя версия", "Latest release"), null) }, SelectedIndex = 0 };
        versions.Children.Add(_releaseChoice);
        Button(versions, "Выбрать выпуск", "Select release", SelectRelease_Click, IconKind.Check);
        _releaseStatus = Text(versions, "", "");
        ReliabilityPanels.Children.Remove(_importCard);
        ConfigurationImportHost.Children.Add(_importCard);
        _gameTimer.Tick += async (_, _) => await ObserveGameAsync();
        Closed += (_, _) => { _gameTimer.Stop(); _observedProcess?.Dispose(); };
    }

    private void ApplyReliabilityLanguage()
    {
        foreach (var label in _reliabilityLabels) label.Set(T(label.Ru, label.En));
        CancelDownloadButton.Content = T("Пауза", "Pause");
        CancelDownloadButton.ToolTip = T("Приостановить загрузку", "Pause download");
    }

    private void RefreshReliabilityVisibility()
    {
        if (_recoveryCard is null) return;
        _recoveryCard.Visibility = _versionCard.Visibility = _activePage == "settings" ? Visibility.Visible : Visibility.Collapsed;
        _importCard.Visibility = Visibility.Collapsed;
        _multiplayerCard.Visibility = Visibility.Collapsed;
        _incidentCard.Visibility = _incident is null ? Visibility.Collapsed : Visibility.Visible;
        _incidentText.Text = _incident ?? "";
        RefreshEnhancements();
    }

    private void RefreshReliabilityStatus()
    {
        if (_rollbackButton is null) return;
        foreach (var button in _reliabilityActions) button.IsEnabled = !_busy && !FeedBlocksActions;
        _rollbackButton.IsEnabled = !_busy && !FeedBlocksActions && _game is not null && new PatchRecovery(_game.Directory).CanRollback;
        _workingButton.IsEnabled = !_busy && _game is not null && File.Exists(Path.Combine(_game.Directory, ".pawpatch", "last-working.json"));
        _copyReadiness.IsEnabled = !_busy && _readiness is { Errors.Count: 0 };
        _releaseChoice.IsEnabled = !_busy;
        if (_readiness is not null && _game is not null)
            try { if (_readinessIdentity != new ModuleInstaller(_game.Directory).LoadState().LastSuccessfulUpdate) InvalidateReadiness(); } catch { InvalidateReadiness(); }
        RussianToggle.IsEnabled = !_busy;
        IgnoreDesyncToggle.IsEnabled = !_busy && CanContinueWithSelectedColors;
        _releaseStatus.Text = _settings.PinnedRelease is null ? T("Выбрана последняя версия", "Following the latest release") : T("Выпуск закреплён: ", "Pinned release: ") + _settings.PinnedRelease[..Math.Min(12,_settings.PinnedRelease.Length)];
        if (PatchVersionText.Text != "-")
            _releaseStatus.Text += "\n" + T("Установлена: ", "Installed: ") + PatchVersionText.Text + "\n" + PatchDownloadedText.Text;
        RefreshReliabilityVisibility();
    }

    private async Task RecoverAndCheckRunsAsync()
    {
        if (_game is not null && !IsGameRunning())
        {
            var count = await new PatchRecovery(_game.Directory).RecoverInterruptedAsync();
            if (count > 0)
            {
                var state = new ModuleInstaller(_game.Directory).LoadState();
                if (state.AppliedSettings is not null) RestoreSettings(state.AppliedSettings, state.ReleaseId);
                _incident = T("Прерванная установка восстановлена из резервной копии.", "An interrupted installation was recovered from its backup.");
            }
        }
        if (App.PreviousUncleanExit) _incident = T("Предыдущий запуск лаунчера завершился без штатного выхода. Доступен архив диагностики.", "The previous launcher session ended unexpectedly. Diagnostics are available.");
        var rollback = Path.Combine(ActivityStore.Root, "update-rollback.txt");
        if (File.Exists(rollback))
        {
            _incident = T("Новая версия лаунчера не запустилась. Предыдущая восстановлена; повтор неисправного обновления заблокирован.", "The launcher update did not start. The previous version was restored and that update is blocked.");
            File.Delete(rollback);
        }
        var run = ActivityStore.Read("game-run");
        if (run is { CleanExit: false })
        {
            if (ActivityStore.IsAlive(run))
            {
                _observedRun = run; _observedProcess = Process.GetProcessById(run.ProcessId); GameProcessExit.RetainHandle(_observedProcess); _workingSaved = run.ReachedWindow; _gameTimer.Start();
            }
            else
            {
                // Older launchers required a 20-second window observation even for exit code 0.
                // A short, normal game session must not turn into a recovery incident on restart.
                if (run.ExitCode != 0)
                    _incident = run.ExitCode is not null
                        ? T("Игра завершилась с ошибкой. Можно восстановить рабочие настройки и собрать диагностику.", "The game exited with an error. Restore working settings or collect diagnostics.")
                        : T("Не удалось подтвердить штатное завершение прошлой игры. Это также возможно после закрытия лаунчера или перезагрузки ПК.", "The previous game exit could not be confirmed. This can also follow closing the launcher or restarting the PC.");
                run.CleanExit = true; ActivityStore.Save("game-run", run);
            }
        }
        RefreshReliabilityVisibility();
    }

    private void RestoreSettings(UserSettings source, string? release)
    {
        var restoredChannel = release is null ? null : _feedClient.LoadArchived(release, source.Channel);
        _initializing = true;
        try
        {
            ConfigurationCode.Apply(source, _settings);
            _settings.PinnedRelease = release;
            _settings.PreparedChannel = null; _settings.PreparedFeedFingerprint = null;
            SyncPatchChannelControls();
            RussianToggle.IsChecked = _settings.RussianLocalization;
            ColorsToggle.IsChecked = _settings.CustomPlayerColors;
            IndependentHostilityToggle.IsChecked = _settings.IndependentHostility;
            AdditionalRoamingToggle.IsChecked = _settings.AdditionalRoamingCompanies;
            SiegeBalanceToggle.IsChecked = _settings.SiegeBalance;
        ImprovedAiToggle.IsChecked = _settings.ImprovedAi;
            PowersShardsToggle.IsChecked = _settings.DisablePowersAndShards;
            SelectOosMode(_settings.DesyncMode); SelectSpawnMode(_settings.RoamingSpawnMode);
            _settingsStore.Save(_settings);
            _channel = restoredChannel;
            _latestChannel = null;
            _offeredModChannel = restoredChannel;
            ResetFeedbackContext();
            InvalidateReadiness();
        }
        finally { _initializing = false; }
    }

    private async void ImportConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var imported = ConfigurationCode.Parse(_importInput.Text);
            RestoreSettings(imported, null);
            CardHighlight.Pulse(_importCard);
            var checkedSuccessfully = await CheckFeedAsync();
            ApplyLanguage();
            if (checkedSuccessfully) ShowToast(() => T("Настройки импортированы. Нажмите «Применить настройки» или запустите игру.", "Settings imported. Use Apply settings or launch the game."));
        }
        catch (FormatException ex) { ShowError(new FormatException(T("Некорректный или неподдерживаемый код конфигурации. ", "Invalid or unsupported configuration code. ") + ex.Message)); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void RollbackPatch_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _game is null) return;
        try
        {
            if (IsGameRunning()) throw new IOException(T("Сначала закройте игру.", "Close the game first."));
            SetBusy(true, T("Возвращаю предыдущую установку…", "Restoring the previous installation…"));
            var installer = new ModuleInstaller(_game.Directory);
            var restored = await new PatchRecovery(_game.Directory).RollbackAsync(installer.LoadState());
            if (restored.AppliedSettings is not null) RestoreSettings(restored.AppliedSettings, restored.ReleaseId);
            else { _settings.PreparedFeedFingerprint = null; _settingsStore.Save(_settings); }
            InvalidateReadiness();
            ShowResult(() => T("Предыдущая установка восстановлена.", "The previous installation was restored."));
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private async void RestoreWorking_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _game is null) return;
        try
        {
            var working = ActivityStore.Working(_game.Directory) ?? throw new IOException(T("Рабочих настроек пока нет.", "No working configuration has been recorded yet."));
            RestoreSettings(working.Settings, working.ReleaseId);
            if (await CheckFeedAsync()) ShowResult(() => T("Рабочие настройки выбраны. Нажмите «Установить / Обновить» или запустите игру.", "Working settings selected. Install/update or launch the game to apply them."));
        }
        catch (Exception ex) { ShowError(ex); }
    }

    public sealed record ReleaseChoice(string Label, string? Id)
    {
        public override string ToString() => Label;
    }
    private async Task LoadVersionChoicesAsync()
    {
        if (_loadingVersions) return;
        _loadingVersions = true;
        var channel = _settings.Channel;
        var mod = _settings.Mod;
        try
        {
            if (channel == "beta" && _latestChannel is not null)
                foreach (var reference in _latestChannel.PreviousReleases.Take(8))
                {
                    if (channel != _settings.Channel || mod != _settings.Mod) return;
                    if (_loadedPrevious.Contains(reference.Url)) continue;
                    try { await _feedClient.GetPreviousAsync(reference, channel); _loadedPrevious.Add(reference.Url); }
                    catch (Exception ex) { ActivityStore.Log(ex); }
                }
            if (channel != _settings.Channel || mod != _settings.Mod) return;
            var options = new List<ReleaseChoice> { new(T("Последняя версия", "Latest release"), null) };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in _feedClient.Archived(channel))
            {
                string content;
                try { content = ModLibrary.ContentId(release, mod); } catch (InvalidDataException) { continue; }
                var id = ChannelFingerprint.Create(release);
                if (!seen.Add(content) && id != _settings.PinnedRelease) continue;
                var core = ModPatchVersion(release, mod) ?? "—";
                options.Add(new($"{GameMod.Name(mod, _text.Language == "ru")} · {ChannelPresentation.Name(channel, _text.Language)} · {release.PublishedAt[..Math.Min(10, release.PublishedAt.Length)]} · {core}", id));
            }
            _releaseChoice.ItemsSource = options;
            _releaseChoice.SelectedItem = options.FirstOrDefault(x => x.Id == _settings.PinnedRelease) ?? options[0];
        }
        finally
        {
            _loadingVersions = false;
            if (channel != _settings.Channel || mod != _settings.Mod) _ = LoadVersionChoicesAsync();
        }
    }

    private async void SelectRelease_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _releaseChoice.SelectedItem is not ReleaseChoice choice) return;
        try
        {
            if (choice.Id is not null) _ = _feedClient.LoadArchived(choice.Id, _settings.Channel);
            var changed = _settings.PinnedRelease != choice.Id;
            _settings.PinnedRelease = choice.Id;
            _offeredModChannel = choice.Id is null ? _latestChannel : _feedClient.LoadArchived(choice.Id, _settings.Channel);
            _channel = _offeredModChannel;
            ResetFeedbackContext();
            _settingsStore.Save(_settings);
            InvalidateReadiness();
            if (changed) CardHighlight.Pulse(_versionCard);
            if (await CheckFeedAsync()) ShowResult(() => T("Выпуск выбран. Установите его или примените настройки перед запуском.", "Release selected. Install it or apply settings before launching."));
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void InvalidateReadinessIfChanged()
    {
        if (_readiness is not null && _checkedConfiguration != EffectiveConfigurationCode + _settings.PinnedRelease + _settings.GamePath) InvalidateReadiness();
    }
    private void InvalidateReadiness()
    {
        _readiness = null; _readinessIdentity = null;
        _detailedComparison = null;
        if (_comparisonDetailsButton is not null) _comparisonDetailsButton.IsEnabled = false;
        if (_comparisonText is not null) _comparisonText.Text = "";
        if (_copyReadiness is not null) _copyReadiness.IsEnabled = false;
        if (_readinessText is not null) _readinessText.Text = T("Нужна новая проверка файлов.", "A new file check is required.");
    }

    private async Task CheckReadinessAsync()
    {
        if (_game is null || _channel is null) throw new IOException(_text["status.notfound"]);
        if (IsGameRunning()) throw new IOException(T("Для проверки закройте игру.", "Close the game before checking."));
        SetBusy(true, T("Считаю отпечаток установленных файлов…", "Hashing installed files…"));
        try
        {
            var state = new ModuleInstaller(_game.Directory).LoadState();
            if (!ModLibrary.IsActive(state, _settings) || UpdateDetector.HasModuleChanges(state, ResolveSelectedPackages(_channel))) throw new IOException(T("Сначала примените выбранные настройки и версию патча.", "Apply the selected settings and patch version first."));
            var critical = await MultiplayerCheck.CriticalAsync(_game.Directory, state, ResolveLaunchExecutable(_game.Directory), !CompatibilityRelevant || DataOnlyMode ? new GameRequirement() : SelectedGameRequirement);
            _fileCheckFailed = critical.Count > 0;
            _readiness = await MultiplayerCheck.CreateAsync(_game.Directory, state, GetEffectiveSettings(), ResolveLaunchExecutable(_game.Directory), _game.SteamBuild ?? "?", _userDataDirectory());
            var integrity = _readiness.Errors.Concat(critical).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _readiness = _readiness with { Errors = integrity };
            if (_readiness.Details is not null) _readiness.Details.IntegrityErrors = integrity;
            _checkedConfiguration = EffectiveConfigurationCode + _settings.PinnedRelease + _settings.GamePath;
            _readinessIdentity = state.LastSuccessfulUpdate;
            var modules = string.Join("\n", state.Modules.OrderBy(x => x.Value.Priority).Select(x => $"{x.Key}: {x.Value.Version}"));
            _readinessText.Text = $"Kohan II {_installedGameVersion} · Steam {_game.SteamBuild}\n{_text["patch.channel"]} {CurrentChannelName()}\n{modules}\n\n{EffectiveConfigurationCode}\n\n{_readiness.Fingerprint}\n\n" +
                TF($"Проверено файлов: {_readiness.Files}, {DateTime.Now:HH:mm:ss}. Ошибок: {_readiness.Errors.Count}.", $"Files checked: {_readiness.Files}, {DateTime.Now:HH:mm:ss}. Errors: {_readiness.Errors.Count}.");
            _fileCheckFailed = _readiness.Errors.Count > 0;
            var verificationErrors = _readiness.Errors.ToArray();
            ShowResult(() => verificationErrors.Length == 0 ? T("Отпечаток готов к сравнению.", "Fingerprint ready to compare.") : T("Найдены изменённые или отсутствующие файлы: ", "Changed or missing files: ") + string.Join(", ", verificationErrors.Take(4)), failure: _fileCheckFailed);
        }
        finally { SetBusy(false); }
    }
    private async void CheckReadiness_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try { InvalidateReadiness(); await CheckReadinessAsync(); } catch (Exception ex) { InvalidateReadiness(); ShowError(ex); }
    }
    private async void CopyReadiness_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            await CheckReadinessAsync();
            if (_readiness is { Errors.Count: 0 }) await CopyTextAsync(_readiness.Fingerprint, () => T("Отпечаток скопирован.", "Fingerprint copied."));
        }
        catch (Exception ex) { InvalidateReadiness(); ShowError(ex); }
    }
    private async void CompareReadiness_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        try
        {
            var peer = _peerInput.Text.Trim().ToUpperInvariant();
            if (peer.Length != 72 || !peer.StartsWith("PAW-MP1-") || !peer[8..].All(Uri.IsHexDigit)) throw new FormatException(T("Вставьте полный отпечаток PAW-MP1-…", "Paste a complete PAW-MP1-… fingerprint."));
            await CheckReadinessAsync();
            var matches = _readiness is { Errors.Count: 0 } && _readiness.Fingerprint == peer;
            _comparisonText.Text = matches ? T("Конфигурации совпадают", "Configurations match") : T("Отпечатки различаются или файлы повреждены. Откройте отчёт друга ниже, чтобы увидеть конкретные компоненты и файлы.", "Fingerprints differ or files are damaged. Open your friend's report below to see specific components and files.");
            _comparisonText.Foreground = (Brush)FindResource(matches ? "SuccessBrush" : "DangerBrush");
        }
        catch (Exception ex) { InvalidateReadiness(); ShowError(ex); }
    }

    private void MarkVisibleChangelogRead()
    {
        var visible = !_changelogTransitionPending && IsLoaded && IsVisible && _historyWindow?.IsActive == true && WindowState != WindowState.Minimized && ChangelogCard.IsVisible;
        if (ChangelogTimeline.MarkViewed(_settings, _renderedHistory, visible)) _settingsStore.Save(_settings);
        RefreshUnreadBadges();
    }
    private void RefreshUnreadBadges()
    {
        if (_settings is null || HistoryUnreadBadge is null) return;
        bool Unread(string subject, string source, string branch) => HistoryRows(subject, source, branch).Any(r => ChangelogTimeline.IsUnread(_settings, r));
        var unread = Unread("launcher", "all", "all") || Unread(_settings.Mod, "all", _settings.Channel);
        HistoryUnreadBadge.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
        if (HistoryButtonBadge is not null) HistoryButtonBadge.Visibility = unread ? Visibility.Visible : Visibility.Collapsed;
        SyncHistoryFilters();
    }

    private void SetUnreadTabContent(Button button, string label, bool unread)
    {
        var identity = label + "|" + unread;
        if (button.Content is StackPanel { Tag: string previous } && previous == identity) return;
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Tag = identity };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        if (unread) panel.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
            Background = ColorBrush("#E55E71"), Margin = new Thickness(7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        button.Content = panel;
        System.Windows.Automation.AutomationProperties.SetName(button, label);
        System.Windows.Automation.AutomationProperties.SetHelpText(button, unread ? T("Есть непрочитанные изменения", "Unread changes") : "");
    }

    private object? _activeTransfer;
    private long? _transferReceived;
    private long? _transferTotal;

    private void FinishTransfer()
    {
        // Progress<T> posts to the UI queue: ignore callbacks still queued after completion.
        var hadTransfer = _activeTransfer is not null;
        _activeTransfer = null;
        StopProgressAnimation();
        if (_busy && hadTransfer)
        {
            // The download helper returns before package verification/extraction. Keep the
            // layout occupied, but retire its speed/ETA immediately instead of showing stale data.
            ShowWorking(() => T("Проверяю загруженные файлы…", "Verifying downloaded files…"));
            SetOperationIndeterminate(true);
            SetTransferDetails(_transferReceived is long received ? T("Получено: ", "Received: ") + FormatBytes(received)
                : T("Подготавливаю локальные файлы…", "Preparing local files…"), phaseChanged: true);
        }
        else if (!_busy)
        {
            _transferReceived = _transferTotal = null;
            RefreshTransferSummary();
            TransferText.Text = "";
            TransferText.ToolTip = null;
            TransferText.Visibility = Visibility.Collapsed;
        }
    }

    private IProgress<(long Received, long? Total)> TransferProgress(string name)
    {
        var transfer = _activeTransfer = new object();
        _transferReceived = _transferTotal = null;
        RefreshTransferSummary();
        SetTransferDetails(T("Подготавливаю загрузку…", "Preparing download…"), phaseChanged: true);
        ShowWorking(() => _text["progress.downloading"] + ": " + name);
        SetOperationProgress(0,animate:false);
        SetOperationIndeterminate(true);
        var watch = Stopwatch.StartNew();
        long? first = null;
        long lastUpdate = -1000;
        return new Progress<(long Received, long? Total)>(value =>
        {
            if (!ReferenceEquals(_activeTransfer, transfer)) return;
            first ??= value.Received;
            if (watch.ElapsedMilliseconds - lastUpdate < 180 && value.Received != value.Total) return;
            lastUpdate = watch.ElapsedMilliseconds;
            var speed = Math.Max(0, value.Received - first.Value) / Math.Max(watch.Elapsed.TotalSeconds, .1);
            var remaining = value.Total is > 0 && speed > 1024 ? TimeSpan.FromSeconds(Math.Clamp((value.Total.Value - value.Received) / speed, 0, 86400)).ToString(@"hh\:mm\:ss") : "-";
            _transferReceived = value.Received;
            _transferTotal = value.Total;
            RefreshTransferSummary();
            SetTransferDetails(FormatBytes(value.Received) + " / " + (value.Total is null ? "?" : FormatBytes(value.Total.Value)) + "\n" + FormatBytes((long)speed) + T("/с · осталось ", "/s · remaining ") + remaining);
            SetOperationIndeterminate(value.Total is null);
            SetOperationProgress(value.Total is > 0 ? Math.Clamp(value.Received * 100d / value.Total.Value, 0, 100) : 0);
        });
    }
    private void CancelDownload_Click(object sender, RoutedEventArgs e) => _downloadCancellation?.Cancel();

    private async Task<string> CreateGameDiagnosticsAsync(string path)
    {
        var installer = new ModuleInstaller(_game!.Directory);
        InstallState state;
        IReadOnlyList<string> errors;
        try { state = installer.LoadState(); errors = await installer.VerifyAsync(); }
        catch (Exception ex) { ActivityStore.Log(ex); state = new InstallState(); errors = [ex.Message]; }
        return await DiagnosticsCollector.CreateAsync(path, _game, GetEffectiveSettings(), state, errors);
    }

    private void BeginGameObservation(Process process, InstallState state)
    {
        ActionJournal.Record("game.started", ConfigurationCode.Create(GetEffectiveSettings()));
        // A headless command exits right after the start; an unobserved run must not become a recovery incident later.
        if (_headless) { _headlessLaunched = process.Id; return; }
        _observedRun = new RunRecord { ProcessId = process.Id, GameRoot = _game!.Directory, Settings = GetEffectiveSettings(), ReleaseId = state.ReleaseId };
        try { _observedRun.StartTicks = process.StartTime.ToUniversalTime().Ticks; } catch { }
        _observedProcess?.Dispose(); _observedProcess = process; _workingSaved = false;
        GameProcessExit.RetainHandle(process);
        ActivityStore.Save("game-run", _observedRun);
        _gameTimer.Start();
    }
    private async Task ObserveGameAsync()
    {
        if (_observing || _observedRun is null) return;
        _observing = true;
        try
        {
            var run = _observedRun;
            if (_observedProcess is null || _observedProcess.HasExited || _observedProcess.MainWindowHandle == IntPtr.Zero)
            {
                var candidates = Process.GetProcesses();
                try
                {
                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            if (candidate.ProcessName.StartsWith("k2", StringComparison.OrdinalIgnoreCase) && candidate.StartTime.ToUniversalTime() >= run.Started.UtcDateTime.AddSeconds(-2) && candidate.MainWindowHandle != IntPtr.Zero &&
                                string.Equals(Path.GetDirectoryName(candidate.MainModule?.FileName), run.GameRoot, StringComparison.OrdinalIgnoreCase))
                            {
                                _observedProcess?.Dispose(); _observedProcess = candidate;
                                GameProcessExit.RetainHandle(candidate);
                                run.ProcessId = candidate.Id; run.StartTicks = candidate.StartTime.ToUniversalTime().Ticks;
                                ActivityStore.Save("game-run", run); break;
                            }
                        }
                        catch { }
                    }
                }
                finally { foreach (var candidate in candidates) if (!ReferenceEquals(candidate, _observedProcess)) candidate.Dispose(); }
            }
            _observedProcess?.Refresh();
            if (_observedProcess is { HasExited: false } && _observedProcess.MainWindowHandle != IntPtr.Zero && DateTimeOffset.UtcNow - run.Started > TimeSpan.FromSeconds(20) && !_workingSaved)
            {
                run.ReachedWindow = true; _workingSaved = true;
                await ActivityStore.SaveWorkingAsync(run); ActivityStore.Save("game-run", run);
            }
            if (_observedProcess is { HasExited: true } && DateTimeOffset.UtcNow - run.Started > TimeSpan.FromSeconds(30))
            {
                // Finish once, even when an externally started process has no exit code.
                // Unknown is not success, but is not evidence of a crash either.
                _gameTimer.Stop();
                run.ExitCode = GameProcessExit.ReadCode(_observedProcess);
                ActionJournal.Record("game.exit", run.ExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown");
                // Window readiness controls last-working promotion above, not the exit verdict.
                // Closing normally before that threshold still reports Windows exit code 0.
                run.CleanExit = run.ExitCode == 0;
                _observedProcess.Dispose(); _observedProcess = null; _observedRun = null;
                ActivityStore.Save("game-run", run);
                if (!run.CleanExit) _incident = run.ExitCode is null
                    ? T("Игра закрыта, но Windows не предоставила код завершения. Это не означает, что игра аварийно завершилась.", "The game closed, but Windows did not provide an exit code. This does not establish that the game crashed.")
                    : T("Игра завершилась с ошибкой. Можно восстановить рабочие настройки и собрать диагностику.", "The game exited with an error. Restore working settings or collect diagnostics.");
                RefreshReliabilityVisibility();
            }
        }
        catch (Exception ex) { ActivityStore.Log(ex); }
        finally { _observing = false; }
    }
}
