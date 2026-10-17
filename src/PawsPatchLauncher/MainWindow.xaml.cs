using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow : Window
{
    private readonly SettingsStore _settingsStore = new();
    private readonly LauncherConfiguration _configuration;
    private readonly FeedClient _feedClient;
    private readonly UserSettings _settings;
    private readonly Localization _text;
    private GameInstallation? _game;
    private ChannelManifest? _channel;
    private bool _busy;
    private bool _initializing = true;
    private bool _colorsAvailable;
    private bool _checkingFeed;
    private bool _backgroundFeedCheck;
    private bool FeedBlocksActions => _checkingFeed && !_backgroundFeedCheck;
    private long _feedCheckVersion;
    private CancellationTokenSource? _feedCancellation;
    private bool _patchUpdateAvailable;
    private bool _patchInstalled;
    private string _activePage = "home";
    private LauncherRelease? _pendingLauncherUpdate;
    private readonly LauncherUpdateState _launcherUpdates = new();
    private bool _launcherCheckFailed;
    private DateTimeOffset? _lastChecked;
    private readonly DispatcherTimer _updateTimer = new();

    public MainWindow() : this(null, null) { }

    public MainWindow(LauncherConfiguration? configuration, FeedClient? feedClient, WindowPlacementStore? windowPlacementStore = null)
    {
        InitializeComponent();
        _ = new WindowWorkArea(this);
        // Keep the larger default usable on small screens and at increased Windows scaling.
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(1, workArea.Width - 32));
        Height = Math.Min(Height, Math.Max(1, workArea.Height - 32));
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        _configuration = configuration ?? SettingsStore.LoadConfiguration();
        _feedClient = feedClient ?? new FeedClient(_configuration);
        _settings = _settingsStore.Load();
        if (!_settings.LargeMapSizes && _settings.PawPatchEnabled && !GameMod.IsVanilla(_settings))
        {
            _settings.LargeMapSizes = true;
            _settings.PreparedFeedFingerprint = null;
            _settingsStore.Save(_settings);
        }
        _text = new Localization(_settings.Language);
        InitializeAccountUi();
        InitializeGameLaunchState();
        InitializeNotificationSound();
        InitializeFeedback();
        InitializeNotifications();
        InitializeReliabilityUi();
        InitializeEnhancementsUi();
        InitializeDiagnosticsUi();
        InitializeConfirmation();
        InitializeAppearance();
        InitializeCommunity();
        InitializeLanguageChoices();
        InitializeActionJournal();
        SyncPatchChannelControls();
        RussianToggle.IsChecked = _settings.RussianLocalization;
        ColorsToggle.IsChecked = _settings.CustomPlayerColors;
        IndependentHostilityToggle.IsChecked = GameMod.IsArcaneWars(_settings) ? _settings.IndependentHostility : GameMod.PureComponents(_settings).IndependentHostility;
        AdditionalRoamingToggle.IsChecked = GameMod.IsArcaneWars(_settings) ? _settings.AdditionalRoamingCompanies : GameMod.PureComponents(_settings).AdditionalRoamingCompanies;
        SiegeBalanceToggle.IsChecked = _settings.SiegeBalance;
        ImprovedAiToggle.IsChecked = GameMod.ImprovedAiSelected(_settings);
        PowersShardsToggle.IsChecked = _settings.DisablePowersAndShards;
        SelectOosMode(_settings.DesyncMode);
        SelectSpawnMode(_settings.RoamingSpawnMode);
        ApplyLanguage();
        SetActivePage("home");
        _updateTimer.Interval = TimeSpan.FromMinutes(1);
        _updateTimer.Tick += async (_, _) => await CheckFeedAsync(background: true);
        Closed += (_, _) => { _updateTimer.Stop(); CancelBackgroundFeed(); };
        Closed += (_, _) => CancelChangelogTransition();
        Closed += (_, _) => CancelAboutTransition();
        Closed += (_, _) => ResetChatMedia(dispose:true);
        Closing += (_, e) =>
        {
            if (ConfirmationActive) { e.Cancel = true; _ = CompleteConfirmationAsync(false); }
            else if (_busy) { e.Cancel = true; ShowToast(() => T("Дождитесь окончания операции или приостановите загрузку.", "Wait for the operation or pause the download.")); }
        };
        _initializing = false;
        Loaded += async (_, _) => { try { await InitializeAsync(); } finally { _initialized.TrySetResult(); } };
        ContentRendered += (_, _) => MarkVisibleChangelogRead();
        Activated += (_, _) => MarkVisibleChangelogRead();
        StateChanged += (_, _) => MarkVisibleChangelogRead();
        // Off-screen preview/smoke fixtures and headless commands never read or overwrite the user's desktop placement.
        if (!HeadlessMode.Active && (!ActivityStore.IsSmokeTest || windowPlacementStore is not null))
            _windowPlacement = new WindowPlacementPersistence(this, windowPlacementStore ?? new WindowPlacementStore(ActivityStore.Root));
    }

    private async Task InitializeAsync()
    {
        if (ActivityStore.IsSmokeTest)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            SelfUpdater.AcknowledgeStartup();
            File.WriteAllText(Path.Combine(ActivityStore.Root, "window-ready.txt"), SelfUpdater.CurrentVersion.ToString(3));
            return;
        }
        try
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            SelfUpdater.AcknowledgeStartup();
            LocateGame();
            await RecoverAndCheckRunsAsync();
        }
        catch (Exception ex) { ShowError(ex); return; }
        if (App.StartupUpdateCompleted)
        {
            _latestChannel = _feedClient.KnownChannel(_settings.Channel) ?? _feedClient.CachedGuideChannel(_settings.Channel, null);
            _channel = _settings.PinnedRelease is null ? _latestChannel : _feedClient.CachedGuideChannel(_settings.Channel, _settings.PinnedRelease);
            _offeredModChannel = _channel;
            await AdoptLocalModsAsync(_channel);
            RefreshNews(); RefreshModuleAvailability(); RefreshStatus();
            _ = CheckFeedAsync(background: true);
        }
        else
        {
            await CheckFeedAsync();
            if (await InstallPendingLauncherUpdateAsync(showErrors: false)) return;
        }
        _updateTimer.Start();
        RefreshStatus();
        await LoadVersionChoicesAsync();
    }

    private void ApplyLanguage()
    {
        ApplyReliabilityLanguage();
        TitleText.Text = _text["app.title"];
        SubtitleText.Text = _text["app.subtitle"];
        if (ActivityStore.LocalTestProfile is not null)
        {
            SubtitleText.Text = T("Тестовая сборка · локальные обновления", "Test build · local updates");
            Title = "Paw's Launcher — Local test";
            if (ActivityStore.IsCommunityTest) SubtitleText.Text = T("Тестовая сборка · общий чат RU / EN", "Test build · RU / EN community chat");
            if (ActivityStore.IsCustomModsTest) SubtitleText.Text = T("Тестовая сборка · пользовательские моды", "Test build · custom mods");
        }
        HomeNav.Content = _text["nav.home"];
        ModulesNav.Content = _text["nav.modules"];
        SettingsNav.Content = _text["nav.settings"];
        AboutNav.Content = T("Справка", "Guide");
        RefreshModNoticeLanguage();
        RefreshAboutPage();
        MinimizeWindowButton.ToolTip = T("Свернуть", "Minimize");
        CloseWindowButton.ToolTip = T("Закрыть", "Close");
        System.Windows.Automation.AutomationProperties.SetName(MinimizeWindowButton, (string)MinimizeWindowButton.ToolTip);
        System.Windows.Automation.AutomationProperties.SetName(CloseWindowButton, (string)CloseWindowButton.ToolTip);
        ApplyPatchChannelLanguage();
        ApplyGameSettingsLanguage();
        HomeWelcomeTitleText.Text = _text["home.welcome.title"];
        HomeWelcomeBodyText.Text = _text["home.welcome.body"];
        SettingsTitleText.Text = _text["settings.title"];
        SettingsLanguageTitleText.Text = _text["settings.language"];
        SettingsLanguageDescriptionText.Text = _text["settings.language.desc"];
        SettingsLanguageButton.Content = UiLanguages.Text(_text.Language, "English", "Русский");
        SettingsRepairTitleText.Text = _text["settings.repair"];
        SettingsRepairDescriptionText.Text = _text["settings.repair.desc"];
        SettingsRepairButton.Content = _text["button.repair"];
        SettingsUpdatesDescriptionText.Text = _text["settings.updates.desc"];
        ApplyRemovalLanguage();
        CheckUpdatesButton.Content = _text["button.checknow"];
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        LauncherVersionLabel.Text = T("ЛАУНЧЕР", "LAUNCHER");
        InstalledPatchLabel.Text = T("УСТАНОВЛЕНО", "INSTALLED");
        LauncherVersionText.Text = $"{version.Major}.{version.Minor}.{version.Build}";
        GamePathLabel.Text = _text["game.path"];
        GameVersionLabel.Text = _text["game.version"].ToUpperInvariant();
        PatchVersionLabel.Text = _text["patch.version"].ToUpperInvariant();
        ModulesTitleText.Text = _text["modules.title"];
        CoreTitleText.Text = _text["modules.core"];
        CoreDescriptionText.Text = _text["modules.core.desc"];
        RussianTitleText.Text = T("Локализация игры", "Game language");
        RussianDescriptionText.Text = T("Общий язык для всех модов и оригинальной игры", "Shared language for all mods and the original game");
        SyncLanguageChoices();
        ColorsTitleText.Text = _text["modules.colors"];
        ColorsDescriptionText.Text = _text.ColorText("modules.colors.desc", _channel);
        OosTitleText.Text = _text["modules.oos"];
        OosDescriptionText.Text = T("Продолжает матч при обнаружении рассинхрона, не устраняя его причину", "Continues the match on desync without fixing its cause");
        ApplySettingsButton.Content = T("Применить настройки", "Apply settings");
        IndependentTitleText.Text = _text["modules.independent"];
        IndependentDescriptionText.Text = _text["modules.independent.desc"];
        RoamingSpawnTitleText.Text = _text["modules.spawn"];
        StandardSpawnRadio.Content = _text["modules.spawn.standard"];
        X2SpawnRadio.Content = "×2";
        X4SpawnRadio.Content = _text["modules.spawn.x4"];
        AdditionalRoamingTitleText.Text = _text["modules.roaming"];
        AdditionalRoamingDescriptionText.Text = _text["modules.roaming.desc"];
        SiegeBalanceTitleText.Text = _text["modules.siege"];
        SiegeBalanceDescriptionText.Text = _text["modules.siege.desc"];
        ImprovedAiTitleText.Text = _text["modules.ai"];
        ImprovedAiDescriptionText.Text = _text["modules.ai.desc"];
        PowersShardsTitleText.Text = _text["modules.powers"];
        System.Windows.Automation.AutomationProperties.SetName(PowersShardsToggle, _text["modules.powers"]);
        RefreshPowersShardsOption();
        MultiplayerNoteText.Text = _text["modules.multiplayer.note"];
        ConfigurationTitleText.Text = _text["configuration.title"];
        ConfigurationDescriptionText.Text = _text["configuration.desc"];
        CopyConfigurationButton.Content = _text["button.copyconfig"];
        DiagnosticsTitleText.Text = _text["diagnostics.title"];
        DiagnosticsDescriptionText.Text = _text["diagnostics.desc"];
        DiagnosticsButton.Content = _text["button.diagnostics"];
        RenderDiagnosticsArchive();
        HelpCloseButton.Content = _text["help.close"];
        RefreshNews();
        UpdateButton.Content = _text["button.install"];
        LaunchButton.Content = _text["button.launch"];
        BrowseButton.Content = _text["button.browse"];
        LanguageButton.Content = UiLanguages.Text(_text.Language, "EN", "RU");
        // Friend profiles reuse the component labels; refresh only after those labels are localized.
        ApplyAccountLanguage();
        ApplyCommunityLanguage();
        ApplyHelpTooltips(this);
        RefreshConfigurationCode();
        RefreshModuleAvailability();
        RefreshStatus();
        RefreshToast();
    }

    private void LocateGame()
    {
        _game = GameLocator.Locate(_configuration.SteamAppId, _settings.GamePath);
        if (_game is not null && !string.Equals(_settings.GamePath, _game.Directory, StringComparison.OrdinalIgnoreCase))
        {
            _settings.GamePath = _game.Directory;
            _settingsStore.Save(_settings);
        }
    }

    private async Task<bool> CheckFeedAsync(bool background = false)
    {
        if (!background) CancelBackgroundFeed();
        if (_checkingFeed || _busy || ConfirmationActive) return false;
        var requestedChannel = _settings.Channel;
        var requestedMod = _settings.Mod;
        var requestedRelease = _settings.PinnedRelease;
        _checkingFeed = true;
        _backgroundFeedCheck = background;
        var version = ++_feedCheckVersion;
        if (!background) SetBusy(true, _text["progress.checking"]);
        var revision = _operationRevision;
        bool IsCurrent() => requestedMod == _settings.Mod && requestedChannel == _settings.Channel && requestedRelease == _settings.PinnedRelease
            && version == _feedCheckVersion && revision == _operationRevision && (!background || !_busy && !ConfirmationActive);
        using var cancellation = new CancellationTokenSource(_feedTimeout);
        _feedCancellation = cancellation;
        var launcherCheck = CheckLauncherUpdateAsync(cancellation.Token, IsCurrent);
        try
        {
            var latest = await _feedClient.GetChannelAsync(requestedChannel, cancellation.Token);
            if (!IsCurrent()) return false;
            if (latest is null) throw new InvalidOperationException(_text["status.feedmissing"]);
            var selected = requestedRelease is null ? latest : _feedClient.LoadArchived(requestedRelease, requestedChannel);
            _latestChannel = latest;
            _offeredModChannel = selected;
            _channel = selected;
            await AdoptLocalModsAsync(selected);
            _feedFailure = null;
            if (_errorFromFeed) ClearFriendlyError();
            _lastChecked = DateTimeOffset.Now;
            RefreshNews();
            RefreshModuleAvailability();
            RefreshStatus();
            if (!background && _game is not null && _channel is not null && !IsGameRunning())
            {
                var installer = new ModuleInstaller(_game.Directory);
                var state = installer.LoadState();
                if (state.AppliedSettings is null && state.Modules.ContainsKey("pawpatch-core") && !UpdateDetector.HasModuleChanges(state, ResolveSelectedPackages(_channel)))
                        await installer.RememberLegacyConfigurationAsync(GetEffectiveSettings(), ChannelFingerprint.Create(_channel));
            }
            if (!IsCurrent()) return false;
            if (!background)
                ShowResult(() => _pendingLauncherUpdate is not null || _patchUpdateAvailable || !_patchInstalled || _game is null || _settingsPending || _installationFailure is not null || _fileCheckFailed || _launcherCheckFailed
                    ? IdleStatus()
                    : T("Проверка завершена. Обновлений нет.", "Check complete. No updates available."));
            return true;
        }
        catch (OperationCanceledException) when (!IsCurrent()) { return false; }
        catch (Exception ex)
        {
            ActivityStore.Log(ex);
            if (!IsCurrent()) return false;
            _offeredModChannel ??= _feedClient.CachedGuideChannel(requestedChannel, requestedRelease);
            _channel ??= _offeredModChannel;
            await AdoptLocalModsAsync(_channel);
            _silentFeedFailure = background;
            SetFriendlyError(ex, fromFeed: true);
            var friendly = _presentedError!;
            _feedFailure = () => friendly.Title(_text.Language);
            if (!background) { _feedback.Clear(); ShowToast(_feedFailure, true); }
            return false;
        }
        finally
        {
            // Also complete the independent check when the patch feed is broken
            // or a pinned patch cannot be loaded. Startup self-update still runs.
            await launcherCheck;
            if (version == _feedCheckVersion)
            {
                _feedCancellation = null;
                _checkingFeed = false;
                _backgroundFeedCheck = false;
                if (!background)
                {
                    SetBusy(false);
                    ApplyLanguage();
                }
                else RefreshStatus();
                RefreshNews();
            }
        }
    }

    private void CancelBackgroundFeed()
    {
        if (!_checkingFeed || !_backgroundFeedCheck) return;
        ++_feedCheckVersion;
        _checkingFeed = false;
        _backgroundFeedCheck = false;
        var cancellation = _feedCancellation;
        _feedCancellation = null;
        cancellation?.Cancel();
    }

    private async Task CheckLauncherUpdateAsync(CancellationToken cancellationToken, Func<bool> isCurrent)
    {
        try
        {
            var release = await _feedClient.GetLauncherUpdateAsync(cancellationToken);
            if (!isCurrent()) return;
            _launcherUpdates.Observe(release);
            _launcherCheckFailed = false;
        }
        catch (Exception error)
        {
            if (!isCurrent()) return;
            _launcherCheckFailed = true;
            ActivityStore.Log(error);
        }
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        InstallState? state = null;
        _installationFailure = null;
        _settingsPending = false;
        try { state = _game is null ? null : new ModuleInstaller(_game.Directory).LoadState(); }
        catch (Exception ex) { _installationFailure = ex.Message; _incident = T("Не удалось прочитать состояние установки: ", "Cannot read the installation state: ") + ex.Message; }
        try { SelectLibraryChannel(); }
        catch (Exception ex) { _installationFailure = ex.Message; }
        if ((_channel is not null || GameMod.IsVanilla(_settings)) && state is not null && _installationFailure is null)
            try { _settingsPending = _selectionRequiresUpdate || UpdateDetector.HasSettingsChanges(state, _channel is null ? [] : ResolveSelectedPackages(_channel), GetEffectiveSettings()); }
            catch (FrequencyUnavailableException) { _settingsPending = true; }
            catch (Exception ex) { _settingsPending = true; _installationFailure = ex.Message; }
        RefreshFriendAppliedState(state);
        RefreshAvailableUpdates(state);
        GamePathText.Text = _game?.Directory ?? "-";
        GameVersionText.Text = _game is null ? "-" : $"{(_game.Branch == "beta" ? "Beta" : "Steam")} · build {_game.SteamBuild ?? "?"}";
        RefreshInstalledPatchVersions(state);
        ReadyStatusText.Text = FrequencyUnavailable ? T("Режим ×2 недоступен", "×2 unavailable in this release")
            : _installationFailure is not null || _fileCheckFailed
            ? T("Нужна проверка файлов", "File check required")
            : _feedFailure is not null
            ? T("Обновления не проверены", "Updates not checked")
            : _game is null
            ? _text["status.notfound"]
            : !_patchInstalled
                ? _text["status.notinstalled"]
            : _patchUpdateAvailable
                ? string.Format(_text["update.patch.title"], CurrentChannelName())
                : _settingsPending ? T("Настройки не применены", "Settings not applied")
                : _channel is null || _lastChecked is null ? T("Обновления не проверены", "Updates not checked")
                : string.Format(_text["patch.ready"], CurrentChannelName());
        var statusKind = _game is null || _installationFailure is not null || _fileCheckFailed ? "danger" : !_patchInstalled || _patchUpdateAvailable || _settingsPending || _feedFailure is not null || _channel is null || _lastChecked is null ? "update" : "ready";
        if (!GameMod.IsArcaneWars(_settings) && _game is not null && _installationFailure is null && !_fileCheckFailed)
        {
            ReadyStatusText.Text = !_patchInstalled ? T("Мод не установлен", "Mod not installed") : _settingsPending ? T("Настройки не применены", "Settings not applied")
                : _patchUpdateAvailable ? T("Доступно обновление мода", "Mod update available") : GameMod.Name(_settings.Mod, _text.Language == "ru") + T(" · Готово к запуску", " · Ready to play");
            statusKind = !_patchInstalled || _settingsPending || _patchUpdateAvailable ? "update" : "ready";
        }
        else if (!_settings.PawPatchEnabled && statusKind == "ready")
            ReadyStatusText.Text = T("Arcane Wars готов к игре", "Arcane Wars ready to play");
        if (_selectionRequiresUpdate && _installationFailure is null && !_fileCheckFailed)
            ReadyStatusText.Text = T("Нужно обновить файлы мода", "Mod files need an update");
        ReadyStatusBadge.ToolTip = _selectionRequiresUpdate ? SelectionUpdateText : null;
        ReadyStatusText.Foreground = (Brush)FindResource(statusKind == "danger" ? "DangerBrush" : statusKind == "update" ? "GoldBrightBrush" : "SuccessBrush");
        ReadyStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusKind == "danger" ? "#3B2226" : statusKind == "update" ? "#40351E" : "#193926"));
        ReadyStatusBadge.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(statusKind == "danger" ? "#844B50" : statusKind == "update" ? "#A9873E" : "#3F8D64"));
        var gameRunning = IsGameRunning();
        UpdateButton.IsEnabled = !_busy && !FeedBlocksActions && !ArcaneAccessBlocked && !gameRunning && _game is not null && (_offeredModChannel ?? _channel) is not null && (!_patchInstalled || _patchUpdateAvailable);
        SettingsRepairButton.IsEnabled = !_busy && _game is not null && state?.Modules.Count > 0;
        RemovePatchButton.IsEnabled = !_busy && !FeedBlocksActions && _game is not null && state?.Modules.Count > 0;
        RemoveLauncherButton.IsEnabled = !_busy && !FeedBlocksActions;
        RefreshGameFolderButton();
        ApplyGameLaunchState(gameRunning);
        ApplySettingsButton.IsEnabled = !_busy && !FeedBlocksActions && !ArcaneAccessBlocked && !gameRunning && !FrequencyUnavailable && !_selectionRequiresUpdate && _installationFailure is null && _game is not null && _patchInstalled && (_channel is not null || GameMod.IsVanilla(_settings)) && _settingsPending;
        ApplySettingsButton.ToolTip = _installationFailure ?? (FrequencyUnavailable ? FrequencyUnavailableText : _settingsPending || !_patchInstalled
            ? T("Применить выбранные компоненты без запуска игры.", "Apply selected components without starting the game.")
            : T("Выбранные настройки уже применены.", "Selected settings are already applied."));
        RefreshApplySettingsVisibility();
        RefreshModControls();
        ColorsToggle.IsEnabled = !_busy && _colorsAvailable;
        IndependentHostilityToggle.IsEnabled = !_busy && CanChangeHostilityWithSelectedColors;
        StandardSpawnRadio.IsEnabled = !_busy;
        X2SpawnRadio.IsEnabled = !_busy && SupportsX2(_channel);
        X2SpawnRadio.ToolTip = SupportsX2(_channel) ? null : T("Для ×2 нужен выпуск патча с пакетами этой частоты.", "×2 requires a patch release containing this frequency.");
        X4SpawnRadio.IsEnabled = !_busy;
        AdditionalRoamingToggle.IsEnabled = !_busy;
        SiegeBalanceToggle.IsEnabled = !_busy;
        ImprovedAiToggle.IsEnabled = !_busy && GameMod.PawPatchSelected(_settings) && !DataOnlyMode && GameMod.HasImprovedAi(_channel, _settings.Mod);
        RefreshPowersShardsOption();
        CopyConfigurationButton.IsEnabled = !_busy;
        DiagnosticsButton.IsEnabled = !_busy;
        RenderDiagnosticsArchive();
        SyncPatchChannelControls();
        CheckUpdatesButton.IsEnabled = !_busy && !FeedBlocksActions;
        LastCheckedText.Text = _lastChecked is null ? "" : string.Format(_text["updates.checked"], _lastChecked.Value.ToString("HH:mm:ss"));
        RefreshConfigurationCode();
        RefreshReliabilityStatus();
        RefreshOperationStatus();
        RefreshCompatibilityControls();
    }

    private void RefreshAvailableUpdates(InstallState? state)
    {
        var launcher = _launcherUpdates.Latest;
        _pendingLauncherUpdate = !ActivityStore.IsCommunityTest && !ActivityStore.IsCustomModsTest && launcher is not null && SelfUpdater.IsNewer(launcher.Version) && launcher.Urls.Count > 0 && !SelfUpdater.IsBlocked(launcher.Sha256)
            ? launcher
            : null;
        LauncherUpdateButton.Visibility = _pendingLauncherUpdate is null ? Visibility.Collapsed : Visibility.Visible;
        if (_pendingLauncherUpdate is not null)
            LauncherUpdateButton.Content = string.Format(_text["button.launcherupdate"], _pendingLauncherUpdate.Version);

        _patchInstalled = state is not null && _selectedModStored;
        _patchUpdateAvailable = !ArcaneAccessBlocked && _patchInstalled && _selectionUpdateAvailable;
        if (!_selectionRequiresUpdate && !ArcaneAccessBlocked && _patchInstalled && _channel is not null && _offeredModChannel is not null
            && state is not null && ModLibrary.IsActive(state, _settings) && CatalogSupportsSelection(_offeredModChannel))
        {
            try { _patchUpdateAvailable = ModLibrary.HasUpdate(_channel, _offeredModChannel, _settings.Mod)
                || state.AppliedSettings is { } applied && GameLanguages.HasUpdate(_channel, _offeredModChannel, applied); }
            catch (FrequencyUnavailableException) { }
            catch (Exception ex) { _installationFailure = ex.Message; }
        }

        var modName = GameMod.Name(_settings.Mod, _text.Language == "ru");
        UpdateNoticeTitleText.Text = _patchInstalled ? T("Доступно обновление: ", "Update available: ") + modName : T("Установите ", "Install ") + modName;
        UpdateNoticeBodyText.Text = _selectionRequiresUpdate ? SelectionUpdateText
            : _patchInstalled ? T("Обновление относится к активному моду и его компонентам.", "This update belongs to the active mod and its components.")
            : T("Мод и его компоненты сохранятся на компьютере. Скачаются только выбранные текст и озвучка; затем их можно применять без интернета.", "The mod and its components will be stored on this computer. Only the selected text and speech will be downloaded, then reused offline.");
        UpdateNoticeBorder.Visibility = _activePage == "home" && !ArcaneAccessBlocked && (_patchUpdateAvailable || !_patchInstalled && _game is not null) ? Visibility.Visible : Visibility.Collapsed;
        UpdateButton.Content = !_patchInstalled
            ? _text["button.install"]
            : _selectionRequiresUpdate
                ? _selectionUpdateAvailable ? T("Обновить и применить", "Update and apply") : T("Нужно обновление", "Update required")
            : _patchUpdateAvailable
                ? T("Обновить ", "Update ") + modName
                : _text["button.installed"];
        UpdateButton.ToolTip = _selectionRequiresUpdate ? SelectionUpdateText : null;
    }

    private bool NeedsChannelPreparation(ChannelManifest channel, InstallState state)
    {
        return UpdateDetector.NeedsDownload(state, ResolveSelectedPackages(channel), _feedClient.IsPackageCached);
    }

    private string CurrentChannelName()
        => ChannelPresentation.Name(_settings.Channel, _text.Language);

    private static void SetChangelogTabState(Button button, bool active)
    {
        button.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#5B451D" : "#1B304D"));
        button.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#D6AA45" : "#526984"));
        button.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(active ? "#FFF4CF" : "#E8EDF5"));
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var target = _offeredModChannel ?? _channel;
        if (_game is null || target is null || _busy || ArcaneAccessBlocked || _selectionRequiresUpdate && !_selectionUpdateAvailable) return;
        try
        {
            SetBusy(true, _text["progress.downloading"]);
            await ApplySelectedConfigurationAsync(target, prepareWholeChannel: true);
            _channel = target;
            ShowResult(() => T("Установка завершена.", "Installation complete."));
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private async Task ApplySelectedConfigurationAsync(ChannelManifest channel, bool prepareWholeChannel = false)
        => await ApplyConfigurationSnapshotAsync(channel, prepareWholeChannel, _settings);

    private async Task ApplyConfigurationSnapshotAsync(ChannelManifest channel, bool prepareWholeChannel, UserSettings selection, Func<Task>? beforeCommit = null, bool requireExactConfiguration = false)
    {
        EnsureModAccess(selection.Mod);
        if (_game is null) throw new InvalidOperationException(_text["status.notfound"]);
        EnsureGameClosed();
        if (GameMod.IsArcaneWars(selection) && selection.RoamingSpawnMode == "x2" && !SupportsX2(channel, selection))
            throw new FrequencyUnavailableException(FrequencyUnavailableText);
        var baseHash = await EnsureSupportedGameAsync(channel);
        var activeSettings = EffectiveSettings.ForFeed(selection, channel);
        var requirement = GameMod.Requirement(channel, selection.Mod);
        activeSettings.DataOnly = GameMod.UsesExecutableFeatures(activeSettings, channel) && requirement.K2ExeSha256.Count > 0 && !GameCompatibilityPolicy.Supports(requirement, baseHash);
        activeSettings = EffectiveSettings.ForFeed(activeSettings, channel);
        if (requireExactConfiguration && !FriendConfiguration.Matches(activeSettings, selection))
            throw new FriendCopyException(() => T("Версия игры не позволяет применить конфигурацию игрока полностью. Ваши настройки не изменены.", "The game version cannot apply the player's full configuration. Your settings were not changed."));
        var selected = GamePackageSelector.Select(channel, activeSettings, activeSettings.RussianLocalization, activeSettings.CustomPlayerColors);
        var prepared = prepareWholeChannel ? await InstallModLibraryAsync(channel, selection.Mod) : null;
        var modules = new Dictionary<string, InstalledModule>(StringComparer.OrdinalIgnoreCase);
        var installer = new ModuleInstaller(_game.Directory);
        foreach (var package in selected.OrderBy(x => x.Priority))
        {
            modules[package.Id] = prepared is not null && prepared.TryGetValue(package.Id, out var stored)
                ? stored : await PrepareStoredComponentAsync(installer, package);
        }
        SetOperationIndeterminate(true);
        ShowWorking(() => prepareWholeChannel ? _text["progress.installing"]
            : T("Применяю выбранный мод и компоненты…", "Applying the selected mod and components…"));
        EnsureModAccess(selection.Mod);
        if (beforeCommit is not null) await beforeCommit();
        EnsureModAccess(selection.Mod);
        EnsureGameClosed();
        if (!baseHash.Equals(await CryptoAndIO.Sha256Async(_game.ExecutablePath), StringComparison.OrdinalIgnoreCase))
            throw new IOException(T("Игра обновилась во время подготовки. Примените настройки ещё раз.", "The game changed during preparation. Apply settings again."));
        await installer.ReconcileAsync(modules, settings: activeSettings, releaseId: ChannelFingerprint.Create(channel),
            gameRequirement: requirement, baseGameSha256: baseHash);
        ActionJournal.Record("configuration.applied", ConfigurationCode.Create(activeSettings));
        await new ModLibrary(_game.Directory).RememberLanguagesAsync(selected);
        if (ModLibrary.Packages(channel, selection.Mod).All(p => LocallyAvailable(installer, p)))
            await new ModLibrary(_game.Directory).RememberAsync(channel, selection.Mod);
        _compatibilityKey = "";
        InvalidateReadiness();
        _fileCheckFailed = false;
        if (prepareWholeChannel)
        {
            selection.PreparedChannel = channel.Channel;
            selection.PreparedFeedFingerprint = ChannelFingerprint.Create(channel);
            if (ReferenceEquals(selection, _settings)) _settingsStore.Save(_settings);
        }
        SetOperationIndeterminate(false);
        SetOperationProgress(100);
    }

    private async Task<string> DownloadPackageAsync(PackageRelease package)
    {
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;
        CancelDownloadButton.Visibility = Visibility.Visible;
        var progress = TransferProgress(ChannelPresentation.PlainPunctuation(package.Name.Get(_text.Language)));
        try { return await _feedClient.DownloadVerifiedAsync(package, progress, cancellation.Token); }
        finally
        {
            FinishTransfer();
            _downloadCancellation = null;
            CancelDownloadButton.Visibility = Visibility.Collapsed;
        }
    }

    private UserSettings GetEffectiveSettings() => EffectiveSettingsForGame(_settings, _channel);
    private string EffectiveConfigurationCode => ConfigurationCode.Create(GetEffectiveSettings());

    private List<PackageRelease> ResolveSelectedPackages(ChannelManifest channel)
    {
        EnsureFrequencyAvailable(channel);
        var active = EffectiveSettingsForGame(_settings, channel);
        return GamePackageSelector.Select(channel, active, active.RussianLocalization, active.CustomPlayerColors);
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_game is null || _busy) return;
        try
        {
            SetBusy(true, _text["button.repair"] + "…");
            var errors = await new ModuleInstaller(_game.Directory).VerifyAsync(allowCustomHotkeys: true);
            _fileCheckFailed = errors.Count > 0;
            ShowResult(() => errors.Count == 0 ? T("Проверка файлов завершена. Ошибок нет.", "File check complete. No errors found.") : string.Join(Environment.NewLine, errors.Take(5)), failure: errors.Count > 0);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) => await LaunchGameAsync();

    private async Task LaunchGameAsync()
    {
        if (_game is null || _busy || _launchStarting || ArcaneAccessBlocked) return;
        if (_settingsPending || !_patchInstalled)
        {
            ShowToast(() => T("Сначала установите мод или примените выбранные настройки.", "Install the mod or apply the selected settings first."));
            return;
        }
        _launchStarting=true;
        try
        {
            EnsureGameClosed();
            if (GameMod.IsVanilla(_settings) && !GameMod.UsesExecutableFeatures(_settings, _channel))
            {
                SetBusy(true, T("Проверяю файлы игры…", "Checking game files…"));
                var vanilla = Path.Combine(_game.Directory, "k2.exe");
                var vanillaInstaller = new ModuleInstaller(_game.Directory);
                var check = (await MultiplayerCheck.CriticalAsync(_game.Directory, vanillaInstaller.LoadState(), vanilla, new GameRequirement()))
                    .Concat(await vanillaInstaller.VerifyAsync(allowCustomHotkeys: true)).ToList();
                if (check.Count != 0) throw new IOException(string.Join("; ", check));
                EnsureGameClosed();
                await GameLobbyPreferences.PrepareForLaunchAsync(_game.Directory, vanillaInstaller.LoadState().AppliedSettings ?? GetEffectiveSettings());
                EnsureGameClosed();
                var original = _startProcess(new ProcessStartInfo(vanilla) { WorkingDirectory = _game.Directory, UseShellExecute = true })
                    ?? throw new IOException("Cannot start Kohan II.");
                BeginGameObservation(original, new ModuleInstaller(_game.Directory).LoadState());
                ShowResult(() => T("Игра запущена.", "Game launched."));
                return;
            }
            if (_channel is null && !await CheckFeedAsync()) return;
            var channel = _channel ?? throw new InvalidOperationException(_text["status.feedmissing"]);
            if (CompatibilityRelevant && !await CheckGameCompatibilityAsync(true) && !DataOnlyMode) return;
            var installer = new ModuleInstaller(_game.Directory);
            var state = installer.LoadState();
            var executable = ResolveLaunchExecutable(_game.Directory);
            var needsApply = UpdateDetector.HasSettingsChanges(state, ResolveSelectedPackages(channel), GetEffectiveSettings()) || !File.Exists(executable);
            if (CompatibilityRelevant && (_installedRuntimeMismatch || DataOnlyMode && needsApply))
            {
                ShowResult(() => T("Перед игрой примените настройки или установите совместимый патч.", "Apply settings or install a compatible patch before playing."), failure: true);
                ShowCompatibilityPopup();
                return;
            }
            if (needsApply)
            {
                ShowResult(() => T("Перед запуском примените выбранные настройки.", "Apply the selected settings before launching."));
                return;
            }
            if (!File.Exists(executable))
                throw new FileNotFoundException(UiLanguages.Text(_text.Language, "Не найден EXE для выбранного набора функций.", "The executable for the selected feature set was not found."), executable);

            SetBusy(true, T("Проверяю критические файлы…", "Checking critical files…"));
            var critical = await MultiplayerCheck.CriticalAsync(_game.Directory, installer.LoadState(), executable, !CompatibilityRelevant || DataOnlyMode ? new GameRequirement() : GameMod.Requirement(channel, _settings.Mod));
            if (!GameMod.IsArcaneWars(_settings) || !_settings.PawPatchEnabled || DataOnlyMode)
                critical = critical.Concat(await installer.VerifyAsync(allowCustomHotkeys: true)).ToList();
            _fileCheckFailed = critical.Count > 0;
            if (_fileCheckFailed) throw new IOException(T("Запуск остановлен: ", "Launch stopped: ") + string.Join("; ", critical.Take(5)));
            if (!DataOnlyMode && GameExecutableSelector.HasMenuRuntime(channel) && Path.GetFileName(executable) != "k2.exe")
                await GameMenuMetadata.WriteAsync(_game.Directory, channel, GetEffectiveSettings());
            EnsureGameClosed();
            EnsureModAccess(_settings.Mod);
            await GameLobbyPreferences.PrepareForLaunchAsync(_game.Directory, state.AppliedSettings ?? GetEffectiveSettings());
            EnsureGameClosed();
            var process = _startProcess(new ProcessStartInfo(executable) { WorkingDirectory = _game.Directory, UseShellExecute = true })
                ?? throw new IOException("Cannot start Kohan II.");
            BeginGameObservation(process, installer.LoadState());
            ShowResult(() => T("Игра запущена.", "Game launched."));
        }
        catch (Exception ex) { ShowError(ex); }
        finally { _launchStarting=false; SetBusy(false); RefreshStatus(); }
    }

    private Func<bool> _gameRunningProbe = DetectGameRunning;
    private bool IsGameRunning() => _gameRunningProbe();

    private static readonly HashSet<string> GameProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "k2",
            "k2_paws_family_herd_relations_1372",
            "k2_paws_sync_family_herd_relations_1372",
            "k2_paws_sync_continue_1372",
            "k2_paws_ui_1372",
            "k2_paws_pure_fixes_1372",
            "k2_paws_pure_colors_1372",
            "k2_paws_pure_sync_1372",
            "k2_paws_pure_colors_sync_1372",
            "k2_paws_menu_1372",
            "k2_paws_lobby_colors_mp_1372_experimental",
            "k2_paws_lobby_colors_mp_sync_1372",
            "k2_paws_lobby_colors_mp_nohostility_1372",
            "k2_paws_lobby_colors_mp_nohostility_sync_1372",
            "k2_aw_family_herd_relations_1372",
            "k2_aw_sync_family_herd_relations_1372",
            "k2_aw_sync_continue_1372",
            "k2_aw_lobby_colors_mp_1372_experimental",
            "k2_aw_lobby_colors_mp_sync_1372",
            "k2_aw_lobby_colors_mp_nohostility_1372",
            "k2_aw_lobby_colors_mp_nohostility_sync_1372"
        };

    private static bool DetectGameRunning()
    {
        // GetProcessesByName enumerates all processes for every name. Inspect one
        // fresh snapshot instead; action-time checks still never use cached results.
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
                try { if (GameProcessNames.Contains(process.ProcessName)) return true; }
                catch (InvalidOperationException) { } // Process exited after enumeration.
                catch (System.ComponentModel.Win32Exception) { } // Inaccessible process.
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private string ResolveLaunchExecutable(string root)
    {
        var name = GameExecutableSelector.Select(_configuration, GetEffectiveSettings(), _channel);
        return Path.Combine(root, name);
    }

    private async Task<string> EnsureSupportedGameAsync(ChannelManifest channel)
    {
        if (_game is null) throw new InvalidOperationException(_text["status.notfound"]);
        // Package selection masks executable features when this hash is unsupported.
        // Recheck immediately before the transaction to detect a concurrent Steam update.
        return await CryptoAndIO.Sha256Async(_game.ExecutablePath);
    }

    private async void LauncherUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await InstallPendingLauncherUpdateAsync(showErrors: true);
    }

    private bool _launcherRestarting;
    private Func<Task> _restartLauncher = () => SelfUpdater.RestartThroughStartupAsync();
    private Action _finishLauncherRestart = () => Application.Current.Shutdown();

    private async Task<bool> InstallPendingLauncherUpdateAsync(bool showErrors)
    {
        if (ActivityStore.IsCommunityTest || ActivityStore.IsCustomModsTest || _headless) return false;
        var release = _pendingLauncherUpdate;
        if (release is null || _busy || ConfirmationActive) return false;
        try
        {
            _launcherRestarting = true;
            SetBusy(true, UiLanguages.Format(_text.Language, $"Обновляю лаунчер до {release.Version}…", $"Updating launcher to {release.Version}…"));
            _settingsStore.Save(_settings);
            _windowPlacement?.SaveOnAcceptedClose();
            await _restartLauncher();
            _busy = false;
            _finishLauncherRestart();
            return true;
        }
        catch (Exception ex)
        {
            _launcherRestarting = false;
            if (showErrors) ShowError(ex);
            else
            {
                ActivityStore.Log(ex); SetFriendlyError(ex);
                var friendly = _presentedError!;
                ShowResult(() => friendly.Title(_text.Language), failure: true);
            }
            SetBusy(false);
            return false;
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = _text["dialog.selectgame"], InitialDirectory = _game?.Directory };
        if (dialog.ShowDialog(this) != true) return;
        var found = GameLocator.Validate(dialog.FolderName, _configuration.SteamAppId);
        if (found is null)
        {
            MessageBox.Show(_text["status.notfound"], _text["error.title"], MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _game = found;
        _settings.GamePath = found.Directory;
        _offeredModChannel ??= _latestChannel;
        ResetFeedbackContext();
        _settingsStore.Save(_settings);
        RefreshStatus();
    }

    private void OptionChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var appearance = CaptureAppearance();
        _settings.RussianLocalization = RussianToggle.IsChecked == true;
        if (ReferenceEquals(sender, ColorsToggle) && _colorsAvailable)
            GameMod.SetColors(_settings, ColorsToggle.IsChecked == true);
        if (GameMod.IsArcaneWars(_settings) && ColorsToggle.IsChecked == true && _settings.DesyncMode == "continue"
            && !GameExecutableSelector.SupportsColorDesyncContinue(_channel))
        {
            _settings.DesyncMode = "official";
            SelectOosMode("official");
            ShowResult(() => UiLanguages.Text(_text.Language, "Для цветов вместе с пропуском рассинхрона выберите последний выпуск. В этом старом выпуске сочетание недоступно.", "Select the latest release to combine colors with desync bypass. This older release does not support the combination."));
        }
        if (GameMod.IsArcaneWars(_settings) && ColorsToggle.IsChecked == true && !GameExecutableSelector.SupportsIndependentColors(_channel))
        {
            _settings.IndependentHostility = true;
            IndependentHostilityToggle.IsChecked = true;
        }
        IgnoreDesyncToggle.IsEnabled = !_busy && CanContinueWithSelectedColors;
        _settingsStore.Save(_settings);
        RefreshConfigurationCode();
        RefreshStatus();
        HighlightAppearanceChanges(appearance);
    }

    private void GameplayOptionChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        var appearance = CaptureAppearance();
        if (!GameMod.IsArcaneWars(_settings))
        {
            var pure = GameMod.PureComponents(_settings);
            if (ReferenceEquals(sender, IndependentHostilityToggle)) pure.IndependentHostility = IndependentHostilityToggle.IsChecked == true;
            if (ReferenceEquals(sender, AdditionalRoamingToggle)) pure.AdditionalRoamingCompanies = AdditionalRoamingToggle.IsChecked == true;
            if (ReferenceEquals(sender, ImprovedAiToggle)) pure.ImprovedAi = ImprovedAiToggle.IsChecked == true;
            if (ReferenceEquals(sender, PureLairToggle)) pure.WoundedLairDefenders = PureLairToggle.IsChecked == true;
            _settingsStore.Save(_settings);
            InvalidateReadiness();
            RefreshConfigurationCode();
            RefreshStatus();
            HighlightAppearanceChanges(appearance);
            return;
        }
        if (!DataOnlyMode) _settings.IndependentHostility = IndependentHostilityToggle.IsChecked == true;
        _settings.AdditionalRoamingCompanies = AdditionalRoamingToggle.IsChecked == true;
        _settings.SiegeBalance = SiegeBalanceToggle.IsChecked == true;
        if (ReferenceEquals(sender, ImprovedAiToggle)) _settings.ImprovedAi = ImprovedAiToggle.IsChecked == true;
        _settings.DisablePowersAndShards = PowersShardsToggle.IsChecked == true;
        _settingsStore.Save(_settings);
        InvalidateReadiness();
        RefreshConfigurationCode();
        RefreshStatus();
        HighlightAppearanceChanges(appearance);
    }

    private void RefreshModuleAvailability()
    {
        RefreshAboutFeed();
        var wasInitializing = _initializing;
        _initializing = true;
        try
        {
        _colorsAvailable = GameMod.IsArcaneWars(_settings) ? _channel?.Packages.Any(x => x.Id.Equals("player-colors", StringComparison.OrdinalIgnoreCase)) == true : GameMod.HasPureOptions(_channel);
        ColorsToggle.IsChecked = _colorsAvailable && GameMod.ColorsSelected(_settings);
        if (GameMod.IsArcaneWars(_settings) && ColorsToggle.IsChecked == true && _settings.DesyncMode == "continue"
            && !GameExecutableSelector.SupportsColorDesyncContinue(_channel))
        {
            _settings.DesyncMode = "official";
            SelectOosMode("official");
            _settingsStore.Save(_settings);
        }
        if (GameMod.IsArcaneWars(_settings) && ColorsToggle.IsChecked == true && !GameExecutableSelector.SupportsIndependentColors(_channel))
        {
            _settings.IndependentHostility = true;
            IndependentHostilityToggle.IsChecked = true;
            _settingsStore.Save(_settings);
        }
        ColorsToggle.IsEnabled = !_busy && _colorsAvailable;
        PowersShardsToggle.IsChecked = _settings.DisablePowersAndShards;
        RefreshPowersShardsOption();
        IgnoreDesyncToggle.IsEnabled = !_busy && CanContinueWithSelectedColors;
        IndependentHostilityToggle.IsChecked = _settings.IndependentHostility;
        AdditionalRoamingToggle.IsChecked = _settings.AdditionalRoamingCompanies;
        SiegeBalanceToggle.IsChecked = _settings.SiegeBalance;
        ImprovedAiToggle.IsChecked = GameMod.ImprovedAiSelected(_settings);
        IndependentHostilityToggle.IsEnabled = !_busy && CanChangeHostilityWithSelectedColors;
        ColorsDescriptionText.Text = _colorsAvailable ? _text.ColorText("modules.colors.desc", _channel) : UiLanguages.Text(_text.Language, "Недоступно в выбранном старом выпуске. Выберите последнюю версию патча.", "Unavailable in this older release. Select the latest patch version.");
        }
        finally { _initializing = wasInitializing; }
    }

    private bool PowersShardsAvailable => _channel?.Packages.Any(p => p.Id.Equals("powers-shards-original", StringComparison.OrdinalIgnoreCase)) == true;

    private void PowersShardsChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _busy) return;
        if (!PowersShardsAvailable && PowersShardsToggle.IsChecked != true)
        {
            PowersShardsToggle.IsChecked = _settings.DisablePowersAndShards;
            ShowToast(() => _text["modules.powers.unavailable"], true);
            return;
        }
        GameplayOptionChanged(sender, e);
    }

    private void RefreshPowersShardsOption()
    {
        var available = PowersShardsAvailable;
        // Never silently change imported/restored settings when using an older feed.
        PowersShardsToggle.IsEnabled = !_busy && (available || !_settings.DisablePowersAndShards);
        PowersShardsDescriptionText.Text = _text[available ? "modules.powers.desc" : "modules.powers.unavailable"];
    }

    private async Task ChangeChannelAsync(bool beta)
    {
        if (_busy || FeedBlocksActions || ConfirmationActive || _initializing) { SyncPatchChannelControls(); return; }
        if (_settings.Channel.Equals(beta ? "beta" : "stable", StringComparison.OrdinalIgnoreCase)) { SyncPatchChannelControls(); return; }
        if (beta && !SelectedModHasBeta) { SyncPatchChannelControls(); return; }
        var appearance = CaptureAppearance();
        _settings.PinnedRelease = null;
        _latestChannel = null;
        _offeredModChannel = null;
        ModChannelSelection.SelectChannel(_settings, beta ? "beta" : "stable");
        ResetFeedbackContext();
        SyncPatchChannelControls();
        _settingsStore.Save(_settings);
        _channel = null;
        RefreshModuleAvailability();
        ApplyLanguage();
        HighlightAppearanceChanges(appearance);
        await CheckFeedAsync();
        await LoadVersionChoicesAsync();
        RefreshStatus();
        if (beta && !_settings.BetaNoticesSeen.Contains(_settings.Mod))
        {
            ShowBetaDetails();
            _settings.BetaNoticesSeen.Add(_settings.Mod);
            _settingsStore.Save(_settings);
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || FeedBlocksActions) return;
        await CheckFeedAsync();
        await LoadVersionChoicesAsync();
        RefreshStatus();
    }

    private void OosToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (sender is CheckBox item)
        {
            var mode = item.IsChecked == true ? "continue" : "official";
            if (mode == "continue" && !CanContinueWithSelectedColors)
            {
                SelectOosMode("official");
                return;
            }
            var appearance = CaptureAppearance();
            GameMod.SetDesync(_settings, mode == "continue");
            _settingsStore.Save(_settings);
            RefreshConfigurationCode();
            RefreshStatus();
            HighlightAppearanceChanges(appearance);
        }
    }

    private void SelectOosMode(string mode)
    {
        IgnoreDesyncToggle.IsChecked = mode.Equals("continue", StringComparison.OrdinalIgnoreCase);
    }

    private bool CanContinueWithSelectedColors => ColorsToggle.IsChecked != true
        || (GameMod.IsArcaneWars(_settings) ? GameExecutableSelector.SupportsColorDesyncContinue(_channel) : GameMod.HasPureOptions(_channel));

    private bool CanChangeHostilityWithSelectedColors => ColorsToggle.IsChecked != true
        || !GameMod.IsArcaneWars(_settings) && GameMod.HasPureBeta(_channel)
        || GameExecutableSelector.SupportsIndependentColors(_channel);

    private void SpawnMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        if (sender is RadioButton item && item.Tag is string mode)
        {
            var appearance = CaptureAppearance();
            if (mode == "x2" && !SupportsX2(_channel)) { SelectSpawnMode(_settings.RoamingSpawnMode); return; }
            string chosen = mode.ToLowerInvariant() is "x2" or "x4" ? mode.ToLowerInvariant() : "standard";
            if (GameMod.IsArcaneWars(_settings)) _settings.RoamingSpawnMode = chosen;
            else GameMod.PureComponents(_settings).RoamingSpawnMode = chosen;
            _settingsStore.Save(_settings);
            RefreshConfigurationCode();
            RefreshStatus();
            HighlightAppearanceChanges(appearance);
        }
    }

    private void SelectSpawnMode(string mode)
    {
        StandardSpawnRadio.IsChecked = mode.Equals("standard", StringComparison.OrdinalIgnoreCase);
        X2SpawnRadio.IsChecked = mode.Equals("x2", StringComparison.OrdinalIgnoreCase);
        X4SpawnRadio.IsChecked = mode.Equals("x4", StringComparison.OrdinalIgnoreCase);
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        var appearance = CaptureAppearance();
        _settings.Language = _text.Language == "ru" ? "en" : "ru";
        _text.SetLanguage(_settings.Language);
        _settingsStore.Save(_settings);
        ApplyLanguage();
        HighlightAppearanceChanges(appearance);
    }

    private void RefreshConfigurationCode()
    {
        if (!_initializing) LogSelectionSnapshot();
        InvalidateReadinessIfChanged();
        var context = EffectiveConfigurationCode + "|" + _settings.GamePath + "|" + _settings.PinnedRelease;
        if (_feedbackContext is not null && context != _feedbackContext && !_feedback.Working)
            _feedback.Clear();
        _feedbackContext = context;
        if (ConfigurationCodeText is not null)
            ConfigurationCodeText.Text = EffectiveConfigurationCode;
    }

    private async void CopyConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        var code = EffectiveConfigurationCode;
        await CopyConfigurationButton.CopyAsync(()=>CopyTextAsync(code, () => _text["configuration.copied"]));
    }

    private async void DiagnosticsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var dialog = new SaveFileDialog
        {
            Title = _text["diagnostics.dialog"],
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"PawsPatch_Diagnostics_{DateTime.Now:yyyy-MM-dd_HHmmss}.zip",
            InitialDirectory = Directory.Exists(downloads) ? downloads : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            AddExtension = true,
            DefaultExt = ".zip"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            SetBusy(true, _text["diagnostics.progress"]);
            var archive = _game is null
                ? await DiagnosticsCollector.CreateLauncherOnlyAsync(dialog.FileName)
                : await CreateGameDiagnosticsAsync(dialog.FileName);
            var remembered = await RememberCreatedDiagnosticsAsync(archive);
            ShowResult(() => string.Format(_text["diagnostics.ready"], archive), duration: TimeSpan.FromSeconds(30));
            if (remembered) ShowToast(() => T("Архив диагностики готов.", "Diagnostic archive is ready."));
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); RefreshStatus(); }
    }

    private IInputElement? _helpPreviousFocus;
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        _socialDetailsPeer=null;
        if (sender is not Button { Tag: string key }) return;
        _helpPreviousFocus = Keyboard.FocusedElement ?? FocusManager.GetFocusedElement(this);
        HelpTitleText.Text = _text[$"{key}.title"] == $"{key}.title" ? _text[key] : _text[$"{key}.title"];
        HelpBodyText.Text = ModuleHelpText(key);
        RenderArcaneWarsCredit(key);
        HelpOverlay.Visibility = Visibility.Visible;
        HelpOverlay.UpdateLayout();
        Motion.Reveal(HelpOverlay);
        RevealDialogCard(HelpCard);
        HelpCloseButton.Focus();
    }

    private async void HelpCloseButton_Click(object sender, RoutedEventArgs e) => await CloseHelpAsync();
    private async Task CloseHelpAsync()
    {
        var previous = _helpPreviousFocus;
        _helpPreviousFocus = null;
        if (await Motion.HideAsync(HelpOverlay) && !ConfirmationActive && ModNoticeOverlay.Visibility != Visibility.Visible)
            (previous as UIElement)?.Focus();
    }
    private async void HelpOverlay_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || ConfirmationActive) return;
        e.Handled = true;
        await CloseHelpAsync();
    }

    private void ApplyHelpTooltips(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is Button { Tag: string key } button
                && (key.StartsWith("modules.", StringComparison.Ordinal) || key is "configuration" or "diagnostics"))
                button.ToolTip = key == "modules.core" ? CoreHelpTooltipText() : ModuleHelpText(key);
            ApplyHelpTooltips(child);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        if (_busy != busy) ActionJournal.Record(busy ? "operation.begin" : "operation.end");
        if (busy) CancelBackgroundFeed();
        if (busy && !_busy) _operationRevision++;
        _busy = busy;
        RefreshOfferActions();
        if (!busy)
        {
            FinishTransfer();
            _feedback.Finish();
        }
        else
        {
            // Capture a localization key when possible, so switching RU/EN cannot leave old text.
            var key = Localization.KeyFor(message);
            ShowWorking(() => key is not null ? _text[key] : message ?? T("Выполняю операцию…", "Operation in progress…"));
            SetOperationProgress(0,animate:false);
        }
        SetOperationIndeterminate(busy);
        RefreshStatus();
    }

    private void ShowError(Exception exception)
    {
        ActionJournal.Record("operation.error", exception.GetType().Name);
        if (_headless) { _headlessFailed = true; _headlessOutput?.WriteLine("detail: " + exception.Message); }
        if (exception is GameAlreadyRunningException)
        {
            ShowResult(() => T("Kohan II уже запущен. Закройте игру перед применением настроек, обновлением или новым запуском.",
                "Kohan II is already running. Close the game before applying settings, updating or launching again."));
            return;
        }
        if (exception is FrequencyUnavailableException)
        {
            ShowResult(() => exception.Message);
            return;
        }
        ActivityStore.Log(exception);
        if (exception is OperationCanceledException)
        {
            ShowResult(() => T("Загрузка приостановлена. При повторной установке она продолжится.", "Download paused. Install again to resume."), failure: true);
            return;
        }
        SetFriendlyError(exception);
        var friendly = _presentedError!;
        ShowResult(() => friendly.Title(_text.Language), failure: true);
        // Details remain available through the error action; ordinary failures use a toast.
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void HomeNav_Click(object sender, RoutedEventArgs e) => SetActivePage("home");
    private void ModulesNav_Click(object sender, RoutedEventArgs e) => SetActivePage("modules");
    private async void SettingsNav_Click(object sender, RoutedEventArgs e) { SetActivePage("settings"); await LoadVersionChoicesAsync(); }

    private void SetActivePage(string page)
    {
        if (_activePage != page) ActionJournal.Record("navigation", page);
        if(page=="admin"&&_account.AdminLevel<1)page="home";
        AdminPanel.Visibility=page=="admin"?Visibility.Visible:Visibility.Collapsed;
        MainOptionsHost.Visibility=page=="admin"?Visibility.Collapsed:Visibility.Visible;
        MainOptionsScroll.Visibility=page=="admin"?Visibility.Collapsed:Visibility.Visible;
        SetNavState(AdminNav,page=="admin");
        if (page == "multiplayer") page = "friends";
        CloseSocialMenu();
        var changed = _activePage != page;
        if (changed && _activePage == "friends") _chatUnreadDivider.End();
        if (changed && page == "account") _accountReturnPage = _activePage;
        _activePage = page;
        var home = page == "home";
        var modules = page == "modules";
        RefreshActionLayout();
        var settings = page == "settings";
        AboutPatchPanel.Visibility = page == "about" ? Visibility.Visible : Visibility.Collapsed;
        AboutModsPanel.Visibility = page == "mods" ? Visibility.Visible : Visibility.Collapsed;
        FriendsPanel.Visibility = page == "friends" ? Visibility.Visible : Visibility.Collapsed;
        FriendsConversationScroll.Visibility = page == "friends" ? Visibility.Visible : Visibility.Collapsed;
        ChangelogCard.Visibility = Visibility.Visible;
        if (!home) CancelChangelogTransition();
        var split = page == "friends";
        Grid.SetColumnSpan(MainOptionsHost, split ? 1 : 3);
        OptionsGapColumn.Width = new GridLength(split ? 20 : 0);
        OptionsColumn.Width = new GridLength(page == "friends" ? .8 : 1.6, GridUnitType.Star);
        NewsColumn.Width = split ? new GridLength(page == "friends" ? 1.5 : 1, GridUnitType.Star) : new GridLength(0);
        RefreshOperationPlacement();
        RefreshCommunityPageLayout();
        AccountPanel.Visibility = page == "account" ? Visibility.Visible : Visibility.Collapsed;
        if (page != "account") ClearAccountPasswords();
        if (page != "about") CancelAboutTransition();
        else if (changed) RefreshAboutPage();

        HomeWelcomeCard.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
        UpdateNoticeBorder.Visibility = home && !ArcaneAccessBlocked && (_patchUpdateAvailable || !_patchInstalled && _game is not null) ? Visibility.Visible : Visibility.Collapsed;
        GameInfoCard.Visibility = home || settings ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        PatchUpdatesCard.Visibility = RecoveryHost.Visibility = RemovalCard.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        ModulesTitleText.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        ModulesHeader.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        MultiplayerNoteCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        CoreModuleCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        RussianModuleCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        ColorsModuleCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        OosModuleCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        IndependentHostilityCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        RoamingSpawnCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        AdditionalRoamingCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        SiegeBalanceCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        PowersShardsCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
        RefreshModControls();
        ConfigurationCodeCard.Visibility = ConfigurationImportHost.Visibility = Visibility.Collapsed;
        DiagnosticsCard.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        if (settings) _ = RefreshDiagnosticsArchiveAsync();
        RefreshReliabilityVisibility();

        SetNavState(HomeNav, home);
        SetNavState(ModulesNav, modules);
        SetNavState(SettingsNav, settings);
        SetNavState(AboutNav, page == "about");
        SetNavState(AboutModsNav, page == "mods");
        SetNavState(FriendsNav, page == "friends");
        AccountHeaderButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(page == "account" ? "#14253B" : "#00000000"));
        AccountHeaderButton.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(page == "account" ? "#354964" : "#00000000"));
        MainOptionsScroll.ScrollToTop();
        if (page == "admin" && changed) AdminContentScroll.ScrollToTop();
        RenderSocialNotifications();
        if (modules) VisitComponents();
        RenderCompatibility();
        if (home) RefreshNews();
        if (changed && !_initializing)
        {
            Motion.Reveal(page == "admin" ? (FrameworkElement)AdminPanel : MainOptionsScroll);
            // Both Friends columns enter together after their first layout.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                if (_activePage != page) return;
                if (page == "friends") Motion.Reveal(FriendsConversationScroll);

            }));
        }
    }

    private static void SetNavState(Button button, bool active)
    {
        var background = (Color)ColorConverter.ConvertFromString(active ? "#314969" : "#1B304D");
        var border = (Color)ColorConverter.ConvertFromString(active ? "#B68D37" : "#526984");
        if (button.Background is not SolidColorBrush current || current.Color != background) button.Background = new SolidColorBrush(background);
        if (button.BorderBrush is not SolidColorBrush edge || edge.Color != border) button.BorderBrush = new SolidColorBrush(border);
    }
}
