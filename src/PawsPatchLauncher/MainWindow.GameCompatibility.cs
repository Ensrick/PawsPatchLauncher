using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private GameCompatibilityState _compatibilityState;
    private string _compatibilityKey = "", _compatibilityPopupKey = "";
    private string _installedGameVersion = "", _gameHash = "", _compatibilityNoticeKey = "";
    private CancellationTokenSource? _compatibilityCancellation;
    private DateTimeOffset _compatibilityRetry;
    private Border? _compatibilityPopup, _compatibilityBackdrop;
    private bool _compatibilityClosed, _launchStarting, _installedRuntimeMismatch, _lastProbeDataOnly;
    private ChannelManifest? _compatiblePatchUpdate;
    private bool _selectedHasInstalledPatch;
    private bool DataOnlyMode => _compatibilityState == GameCompatibilityState.Unsupported || _compatibilityState == GameCompatibilityState.Checking && _lastProbeDataOnly;
    private bool CompatibilityProblem => DataOnlyMode || _installedRuntimeMismatch;
    // Kohan II 1.3.68, the Steam public build 24805354. Paw's engine fixes do not run on it, but players choose it on
    // purpose, so it is a known file-changes-only version: no startup popup and no "unsupported" wording.
    private const string FileOnlyGameSha256 = "CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A";
    private bool KnownFileOnlyGame => DataOnlyMode && !_installedRuntimeMismatch && _compatiblePatchUpdate is null
        && string.Equals(_gameHash, FileOnlyGameSha256, StringComparison.OrdinalIgnoreCase);
    private bool CompatibilityRelevant => GameMod.UsesExecutableFeatures(_settings, _channel);
    private GameRequirement SelectedGameRequirement => _channel is null ? new() : GameMod.Requirement(_channel, _settings.Mod);
    private bool CompatibilityUnread => CompatibilityProblem && _settings.CompatibilityNoticeRead != _compatibilityNoticeKey;
    private bool CompatibilityBlocksLaunch => _compatibilityState is GameCompatibilityState.Checking or GameCompatibilityState.Unavailable
        || _installedRuntimeMismatch || DataOnlyMode && (_settingsPending || _installationFailure is not null || !_patchInstalled);

    private string CompatibilityKey(GameInstallation game, GameRequirement requirement)
    {
        GameFileStamp stamp, installed;
        try { stamp = GameFileStamp.Read(game.ExecutablePath); installed = GameFileStamp.Read(Path.Combine(game.Directory, ".pawpatch", "state.json")); }
        catch { stamp = installed = new(-1, 0); }
        return _settings.Mod + "|" + CompatibilityRelevant + "|" + game.ExecutablePath + "|" + stamp + "|" + installed + "|" + requirement.Version + "|" + requirement.SteamBuild
            + "|" + string.Join(",", requirement.K2ExeSha256).ToUpperInvariant()
            + "|" + (_latestChannel is null ? "" : ChannelFingerprint.Create(_latestChannel));
    }

    private void RefreshCompatibility()
    {
        if (_compatibilityClosed) return;
        if (!ActivityStore.IsSmokeTest && !_busy)
        {
            if (_game is not null && _channel is not null)
            {
                if (CompatibilityKey(_game, SelectedGameRequirement) != _compatibilityKey || _compatibilityState == GameCompatibilityState.Unavailable && DateTimeOffset.UtcNow >= _compatibilityRetry)
                    _ = CheckGameCompatibilityAsync(false);
            }
            else if (_compatibilityKey.Length > 0)
            {
                _compatibilityCancellation?.Cancel(); _compatibilityKey = "";
                _compatibilityState = GameCompatibilityState.Unchecked; _installedRuntimeMismatch = false;
                _gameHash = _installedGameVersion = ""; _compatiblePatchUpdate = null;
            }
        }
        RenderCompatibility();
    }

    private async Task<bool> CheckGameCompatibilityAsync(bool force)
    {
        if (_game is null || _channel is null || _compatibilityClosed) return false;
        var game = _game; var requirement = SelectedGameRequirement; var hashes = requirement.K2ExeSha256.ToArray();
        var key = CompatibilityKey(game, requirement);
        if (!force && key == _compatibilityKey && _compatibilityState is not (GameCompatibilityState.Unchecked or GameCompatibilityState.Unavailable))
            return _compatibilityState == GameCompatibilityState.Supported;
        _compatibilityCancellation?.Cancel();
        var cancellation = new CancellationTokenSource(); _compatibilityCancellation = cancellation;
        _compatibilityKey = key; _compatibilityState = GameCompatibilityState.Checking;
        RenderCompatibility(); LaunchButton.IsEnabled = false;
        try
        {
            var probe = await Task.Run(async () =>
            {
                var before = GameFileStamp.Read(game.ExecutablePath);
                var hash = await CryptoAndIO.Sha256Async(game.ExecutablePath, cancellation.Token);
                var version = InstalledGameVersion.Read(game.ExecutablePath);
                if (before != GameFileStamp.Read(game.ExecutablePath)) throw new IOException("Game changed during verification.");
                return (Hash: hash, Version: version);
            });
            if (cancellation.IsCancellationRequested || _compatibilityClosed || !ReferenceEquals(_compatibilityCancellation, cancellation)) return false;
            if (_game is null || _channel is null || key != CompatibilityKey(_game, SelectedGameRequirement))
            {
                _compatibilityKey = ""; _compatibilityState = GameCompatibilityState.Checking;
                return false;
            }
            _gameHash = probe.Hash; _installedGameVersion = probe.Version ?? "";
            _compatibilityState = hashes.Length == 0 ? GameCompatibilityState.Unchecked
                : hashes.Contains(probe.Hash, StringComparer.OrdinalIgnoreCase) ? GameCompatibilityState.Supported : GameCompatibilityState.Unsupported;
            _lastProbeDataOnly = _compatibilityState == GameCompatibilityState.Unsupported;
            var installed = new ModuleInstaller(game.Directory).LoadState();
            GameRequirement? archived = null;
            if (installed.GameRequirement is null && installed.ReleaseId is { Length: > 0 } id)
                try { archived = GameMod.Requirement(_feedClient.LoadArchived(id, installed.AppliedSettings?.Channel ?? _settings.Channel), installed.AppliedSettings?.Mod ?? _settings.Mod); } catch { }
            _installedRuntimeMismatch = GameCompatibilityPolicy.SelectedRuntimeNeedsUpdate(installed, _settings, probe.Hash, archived,
                _compatibilityState == GameCompatibilityState.Supported);
            _selectedHasInstalledPatch = _selectedInstalledRelease is not null
                || ModLibrary.IsActive(installed, _settings) && GameCompatibilityPolicy.HasNativeModules(installed);
            _compatiblePatchUpdate = GameCompatibilityPolicy.CanOfferUpdate(_selectedHasInstalledPatch, CompatibilityRelevant,
                CompatibilityProblem, _channel, _latestChannel, _settings.Mod, probe.Hash) ? _latestChannel : null;
            ActionJournal.Record("compatibility.checked", $"mod={_settings.Mod};state={_compatibilityState};update={(_compatiblePatchUpdate is not null)}");
            _compatibilityNoticeKey = _settings.Mod + "|" + probe.Hash + "|" + requirement.Version + "|" + string.Join(",", hashes)
                + "|" + (_compatiblePatchUpdate is null ? "" : ChannelFingerprint.Create(_compatiblePatchUpdate));
            if (_activePage == "modules") MarkCompatibilityNoticeRead();
            InvalidateReadiness();
            return _compatibilityState is GameCompatibilityState.Supported or GameCompatibilityState.Unchecked;
        }
        catch (OperationCanceledException) { return false; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(_compatibilityCancellation, cancellation))
            {
                _compatibilityState = GameCompatibilityState.Unavailable; _compatiblePatchUpdate = null;
                _installedRuntimeMismatch = false; _gameHash = _installedGameVersion = ""; ActivityStore.Log(error);
            }
            return false;
        }
        finally
        {
            _compatibilityRetry = DateTimeOffset.UtcNow.AddSeconds(10);
            if (ReferenceEquals(_compatibilityCancellation, cancellation) && !_compatibilityClosed) RefreshStatus();
            cancellation.Dispose();
            if (ReferenceEquals(_compatibilityCancellation, cancellation)) _compatibilityCancellation = null;
        }
    }

    private UserSettings EffectiveSettingsForGame(UserSettings selection, ChannelManifest? channel)
    {
        var active = EffectiveSettings.ForFeed(selection, channel);
        active.DataOnly = GameMod.UsesExecutableFeatures(active, channel) && DataOnlyMode;
        return EffectiveSettings.ForFeed(active, channel);
    }

    private string SupportedGameDescription() => _channel is null ? "" : T("Поддерживается Kohan II ", "Supported Kohan II version: ")
        + SelectedGameRequirement.Version + " · Steam build " + SelectedGameRequirement.SteamBuild;

    private string CompatibilityExplanation()
    {
        if (_compatiblePatchUpdate is not null)
            return T("Для установленной игры уже доступен совместимый патч. Обновите патч, чтобы восстановить все выбранные функции и запустить игру.",
                "A compatible patch is available for the installed game. Update the patch to restore your selected features and play.");
        if (KnownFileOnlyGame)
            return TF($"Это публичная версия Kohan II в Steam. Исправления движка Paw's Patch работают только на {SelectedGameRequirement.Version}, поэтому здесь применяются только файловые изменения. Для сетевой игры всем игрокам нужны одна и та же версия игры и один и тот же выбор.",
                $"This is the public Steam version of Kohan II. Paw's engine fixes run only on {SelectedGameRequirement.Version}, so here the launcher applies file changes only. For multiplayer, every player needs the same game version and the same selection.")
                + T("\n\nПеред игрой нажмите «Применить настройки». Прежний выбор функций сохранён.",
                    "\n\nBefore playing, use Apply settings. Your feature choices are remembered.");
        var direction = InstalledGameVersion.Compare(_installedGameVersion, _game?.SteamBuild, SelectedGameRequirement, _compatibilityState);
        var action = direction switch
        {
            GameVersionRelation.Older => TF($"Обновите игру до версии {SelectedGameRequirement.Version}. Если она доступна только в тестовой ветке: Steam → Свойства игры → Бета-версии → выберите нужную ветку.", $"Update the game to {SelectedGameRequirement.Version}. If this version is only available on a test branch: Steam → Properties → Betas → select that branch."),
            GameVersionRelation.Newer => T("Игра новее поддерживаемой версии. Дождитесь обновления Paw's Patch для этой версии игры.",
                "The game is newer than the supported version. Wait for a Paw's Patch update for this game version."),
            _ => T("Файл игры отличается от поддерживаемого. Проверьте целостность файлов в Steam и выбранную ветку игры. По этому файлу нельзя надёжно определить, старее он или новее.",
                "The game executable differs from the supported one. Verify game files in Steam and check the selected branch. This file cannot reliably be identified as older or newer.")
        };
        return T("Некоторые функции патча недоступны из-за несовместимой версии игры. Изменения EXE отключены; файловые компоненты остаются доступны.\n\n",
            "Some patch features are unavailable because the game version is incompatible. EXE changes are disabled; file components remain available.\n\n") + action
            + T("\n\nПеред игрой нажмите «Применить настройки». Прежний выбор функций сохранён и вернётся после установки совместимого патча.",
                "\n\nBefore playing, use Apply settings. Your previous feature choices are remembered and will return when a compatible patch is installed.");
    }

    private void MarkCompatibilityNoticeRead()
    {
        if (!CompatibilityUnread) return;
        _settings.CompatibilityNoticeRead = _compatibilityNoticeKey;
        try { _settingsStore.Save(_settings); } catch (Exception error) { ActivityStore.Log(error); }
        RefreshModNoticeBadge();
    }

    private void RefreshCompatibilityControls()
    {
        var previous = _initializing; _initializing = true;
        try
        {
            PawCompatibilityButton.Visibility = DataOnlyMode && (GameMod.IsArcaneWars(_settings) || GameMod.HasPureFixes(_channel)) ? Visibility.Visible : Visibility.Collapsed;
            CoreHelpButton.Visibility = PawCompatibilityButton.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            var coreTooltip = CoreHelpTooltipText();
            if (!Equals(CoreHelpButton.ToolTip, coreTooltip)) CoreHelpButton.ToolTip = coreTooltip;
            var frequencyTooltip = ModuleHelpText("modules.spawn");
            if (!Equals(SpawnHelpButton.ToolTip, frequencyTooltip)) SpawnHelpButton.ToolTip = frequencyTooltip;
            PawCompatibilityButton.ToolTip = T("Какие функции Paw's Patch работают", "Available Paw's Patch features");
            AutomationProperties.SetName(PawCompatibilityButton, (string)PawCompatibilityButton.ToolTip);
            CoreDescriptionText.Text = DataOnlyMode ? T("Работают только файловые изменения. Подробнее — в красном значке.", "Only file changes are active. See the red icon for details.")
                : GameMod.IsArcaneWars(_settings) ? _text["modules.core.desc"]
                : GameMod.HasPureBeta(GuideChannel()) ? T("Экономика союзника, камера, интерфейс, карты и исправления игры", "Allied economy, camera, interface, maps and game fixes")
                : T("Исправления цветов значков, отображения лимита рот и генерации рельефа", "Fixes for badge colors, company limit display and terrain generation");
            RefreshPawComponentDependency();
        }
        finally { _initializing = previous; }
    }

    private void RenderCompatibility()
    {
        // The game's version remains incompatible when a mod or its native
        // fixes are switched off. Feature selection only governs launch gating.
        var problem = CompatibilityProblem;
        var unreadable = _compatibilityState == GameCompatibilityState.Unavailable;
        InstalledGameLabel.Text = T("ВЕРСИЯ ИГРЫ", "GAME VERSION");
        InstalledGameText.Text = _game is null ? "—" : _installedGameVersion.Length > 0 ? _installedGameVersion : T("Не определена", "Unknown");
        var fileOnly = KnownFileOnlyGame;
        InstalledGameText.Foreground = SocialBrush((problem && !fileOnly) || unreadable ? "#FF9D9D" : "#A9B7CB");
        GameCompatibilityButton.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
        GameCompatibilityButton.ToolTip = T("Совместимость игры и патча", "Game and patch compatibility");
        AutomationProperties.SetName(GameCompatibilityButton, (string)GameCompatibilityButton.ToolTip);
        GameCompatibilityHint.Text = problem ? fileOnly ? T("Только файловые изменения на этой версии игры.", "File changes only on this game version.")
            : _compatiblePatchUpdate is null
            ? T("Некоторые функции недоступны для этой версии игры.", "Some features are unavailable for this game version.")
            : T("Доступен совместимый патч. Обновите его перед игрой.", "A compatible patch is available. Update before playing.")
            : unreadable ? T("Не удалось проверить файлы игры. Запуск недоступен.", "Could not verify game files. Launch unavailable.") : "";
        GameCompatibilityHint.ToolTip = SupportedGameDescription();
        GameCompatibilityHint.Visibility = problem || unreadable ? Visibility.Visible : Visibility.Collapsed;
        CompatibilityBanner.Visibility = problem && _activePage == "modules" ? Visibility.Visible : Visibility.Collapsed;
        CompatibilityBannerText.Text = _compatiblePatchUpdate is not null
            ? T("Вышел совместимый патч. Обновите его, чтобы использовать все функции.", "A compatible patch is available. Update it to use all features.")
            : fileOnly ? TF($"На этой версии игры применяются только файловые изменения. Исправлениям движка нужна версия {SelectedGameRequirement.Version}.",
                $"This game version runs file changes only. Paw's engine fixes need {SelectedGameRequirement.Version}.")
            : T("Некоторые функции патча недоступны из-за несовместимой версии игры. Файловые изменения можно применить.",
                "Some patch features are unavailable because the game version is incompatible. File changes can be applied.");
        RefreshCompatibilityControls(); RefreshModNoticeBadge();
        if ((problem && !fileOnly) || unreadable)
        {
            ReadyStatusText.Text = problem ? _compatiblePatchUpdate is null ? T("Доступны файловые изменения", "File changes available")
                : T("Обновите патч", "Update the patch") : T("Нужна проверка файлов", "File check required");
            ReadyStatusText.Foreground = SocialBrush("#FF9D9D"); ReadyStatusBadge.Background = SocialBrush("#3B2226"); ReadyStatusBadge.BorderBrush = SocialBrush("#844B50");
        }
        if (!problem)
        {
            if (_compatibilityState != GameCompatibilityState.Checking) { CloseCompatibilityPopup(); _compatibilityPopupKey = ""; }
            return;
        }
        var identity = _compatibilityNoticeKey.Length > 0 ? _compatibilityNoticeKey : _compatibilityKey;
        if (_compatibilityPopup is not null) { _compatibilityPopupKey = identity; return; }
        if (_selectedHasInstalledPatch && _compatibilityState != GameCompatibilityState.Checking && !fileOnly
            && _compatibilityPopup is null && _compatibilityPopupKey != identity && !_busy && !ConfirmationActive && ModNoticeOverlay.Visibility != Visibility.Visible && GameSettingsOverlay.Visibility != Visibility.Visible)
            ShowCompatibilityPopup();
    }

    private void CloseCompatibilityPopup()
    {
        var host = (Grid)ToastHost.Parent;
        if (_compatibilityPopup is null && _compatibilityBackdrop is null) return;
        if (_compatibilityPopup is not null) host.Children.Remove(_compatibilityPopup);
        if (_compatibilityBackdrop is not null) host.Children.Remove(_compatibilityBackdrop);
        _compatibilityPopup = _compatibilityBackdrop = null;
        MainBody.IsHitTestVisible = !ConfirmationActive && ModNoticeOverlay.Visibility != Visibility.Visible;
    }

    private void GameCompatibility_Click(object sender, RoutedEventArgs e) => ShowCompatibilityPopup();
    private void PawCompatibility_Click(object sender, RoutedEventArgs e) => ShowCompatibilityPopup(pawDetails: true);

    private void ShowCompatibilityPopup(bool pawDetails = false)
    {
        if (_busy || ConfirmationActive || ModNoticeOverlay.Visibility == Visibility.Visible || _headless) return;
        ActionJournal.Record("dialog.compatibility", pawDetails ? "component" : "game");
        CloseCompatibilityPopup();
        _compatibilityPopupKey = _compatibilityNoticeKey.Length > 0 ? _compatibilityNoticeKey : _compatibilityKey;
        var body = new StackPanel(); var heading = new DockPanel();
        var close = new Button { Style = (Style)FindResource("GhostButton"), Content = new LauncherIcon { Kind = IconKind.Close }, Width = 30, Height = 30, Padding = new(0), Margin = new(14, 0, 0, 0), ToolTip = T("Закрыть", "Close") };
        DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close);
        heading.Children.Add(new TextBlock { Text = pawDetails ? T("Paw's Patch: доступные функции", "Paw's Patch: available features")
            : _compatiblePatchUpdate is not null ? T("Нужно обновить патч", "Patch update required")
            : KnownFileOnlyGame ? T("Только файловые изменения", "File changes only") : T("Версия игры не поддерживается", "Unsupported game version"),
            FontSize = 20, FontWeight = FontWeights.SemiBold, Foreground = SocialBrush("#FFD0D0"), TextWrapping = TextWrapping.Wrap });
        body.Children.Add(heading);
        body.Children.Add(new TextBlock { Text = T("Установлена игра: ", "Installed game: ") + (_installedGameVersion.Length > 0 ? _installedGameVersion : T("версия не определена", "unknown version"))
            + "\n" + SupportedGameDescription(), FontSize = 13, Foreground = SocialBrush("#FFE3DC"), Margin = new(0, 16, 0, 12), TextWrapping = TextWrapping.Wrap });
        var details = new TextBlock { Text = pawDetails ? PartialPawDescription() : CompatibilityExplanation(), Foreground = SocialBrush("#E9B9BC"), FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        body.Children.Add(new ScrollViewer { Content = details, MaxHeight = Math.Max(180, Math.Min(380, ActualHeight - 330)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var action = new Button { Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 18, 0, 0), Padding = new(18, 10, 18, 10),
            Content = pawDetails ? T("Понятно", "Got it") : _compatiblePatchUpdate is not null ? T("Обновить патч", "Update patch") : T("К компонентам", "Go to components"),
            Background = SocialBrush("#66333D"), Foreground = SocialBrush("#FFE3E7") };
        body.Children.Add(action);
        var popup = RestrictionCard(""); popup.Child = body; popup.MaxWidth = 660; popup.Padding = new(24); popup.Margin = new(24); popup.HorizontalAlignment = HorizontalAlignment.Center; popup.VerticalAlignment = VerticalAlignment.Center;
        _compatibilityPopup = popup;
        var backdrop = new Border { Background = SocialBrush("#B8070C18") }; _compatibilityBackdrop = backdrop;
        var host = (Grid)ToastHost.Parent; Grid.SetRowSpan(backdrop, 2); Panel.SetZIndex(backdrop, 940); host.Children.Add(backdrop);
        Grid.SetRowSpan(popup, 2); Panel.SetZIndex(popup, 950); host.Children.Add(popup); MainBody.IsHitTestVisible = false;
        close.Click += async (_, _) => { if (await Motion.HideAsync(popup)) CloseCompatibilityPopup(); };
        action.Click += async (_, _) =>
        {
            var update = !pawDetails ? _compatiblePatchUpdate : null;
            CloseCompatibilityPopup();
            if (pawDetails) return;
            if (update is null) { SetActivePage("modules"); VisitComponents(); return; }
            await InstallCompatiblePatchAsync(update);
        };
        popup.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; CloseCompatibilityPopup(); } };
        System.Windows.Input.KeyboardNavigation.SetTabNavigation(popup, System.Windows.Input.KeyboardNavigationMode.Cycle);
        System.Windows.Input.KeyboardNavigation.SetControlTabNavigation(popup, System.Windows.Input.KeyboardNavigationMode.Cycle);
        backdrop.MouseDown += async (_, e) => { e.Handled = true; if (await Motion.HideAsync(popup)) CloseCompatibilityPopup(); };
        Motion.Reveal(backdrop); RevealDialogCard(popup); action.Focus();
    }

    private string PartialPawDescription()
    {
        if (!GameMod.IsArcaneWars(_settings)) return PureFixesDescription(true);
        var text = T("Работают:\n• До 16 королевств и 8 команд.\n• Карты 1024×1024 и 1152×1152.\n• Камера WASD и союзная метка F в профиле Dvorak.\n• Исправление цветов значков рот.\n\nНедоступны:\n• Случайный тип карты и время суток с сохранением настроек.\n• Версии модов в главном меню и исправление «−0» в лимите рот.",
            "Available:\n• Up to 16 kingdoms and 8 teams.\n• 1024×1024 and 1152×1152 maps.\n• WASD camera and F allied marker in the Dvorak profile.\n• Company badge color fixes.\n\nUnavailable:\n• Random map type and time of day with saved settings.\n• Mod versions in the main menu and the negative-zero fix in the company limit.");
        if (_settings.Channel == "beta")
        {
            var titles = _channel?.PatchGuide?.Entries.Where(e => e.Category == "beta").Select(e => "• " + e.Title(_text.Language)).ToArray() ?? [];
            if (titles.Length > 0) text += "\n" + string.Join("\n", titles);
        }
        return text + T("\n\nОстальные компоненты выбираются отдельно. На карте 1152×1152 возможны вылеты из-за ограничений игры.",
            "\n\nOther components are selected separately. 1152×1152 maps may crash due to game limits.");
    }

    private async Task InstallCompatiblePatchAsync(ChannelManifest update)
    {
        if (_busy || _game is null) return;
        try
        {
            EnsureGameClosed(); CancelBackgroundFeed();
            _settings.PinnedRelease = null; _settings.Channel = update.Channel; _channel = _latestChannel = _offeredModChannel = update;
            _settingsStore.Save(_settings);
            SetBusy(true, T("Обновляю патч…", "Updating patch…"));
            await ApplySelectedConfigurationAsync(update, true);
            ShowResult(() => T("Патч обновлён. Выбранные функции восстановлены.", "Patch updated. Your selected features are restored."));
        }
        catch (Exception error) { ShowError(error); }
        finally { SetBusy(false); ApplyLanguage(); RefreshStatus(); }
    }
}
