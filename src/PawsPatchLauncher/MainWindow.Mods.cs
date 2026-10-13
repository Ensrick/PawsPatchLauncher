using System.Windows;
using System.Windows.Controls;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private string CoreHelpText()
    {
        if (!GameMod.IsArcaneWars(_settings)) return PureFixesDescription(DataOnlyMode);
        if (DataOnlyMode) return PartialPawDescription();
        var text = _text["modules.core.help"];
        var beta = _settings.Channel == "beta"
            ? _channel?.PatchGuide?.Entries.Where(e => e.Category == "beta").Select(e => e.Title(_text.Language)).ToArray() ?? []
            : [];
        if (beta.Length > 0) text += T("\n\nТакже в выбранной бете:\n", "\n\nAlso in the selected Beta:\n") + string.Join("\n", beta.Select(t => "• " + t));
        return text;
    }

    private string CoreHelpTooltipText() => CoreHelpText() + (GameMod.IsArcaneWars(_settings)
        ? "\n\n" + ArcaneWarsAuthorText + "\n" + ArcaneWarsDiscordInvite : "");

    private string ModuleHelpText(string key) => key switch
    {
        "modules.core" => CoreHelpText(),
        "modules.spawn" => SelectedPatchGuide()?.Entries.FirstOrDefault(e => e.Id == "frequency")?.Body(_text.Language)
            ?? _text["modules.spawn.help"],
        "modules.colors" => ColorHelpText(),
        "modules.siege" => _text[GameMod.HasImprovedAi(GuideChannel()) ? "modules.siege.beta.help" : "modules.siege.help"],
        _ => _text[key + ".help"]
    };
    private PatchGuideDocument? SelectedPatchGuide() => GameMod.IsArcaneWars(_settings)
        ? GuideChannel()?.PatchGuide : GuideChannel()?.ModGuides.FirstOrDefault(g => g.Id == _settings.Mod)?.PatchGuide;
    private string ColorHelpText()
    {
        var feed = GuideChannel();
        var guide = GameMod.IsArcaneWars(_settings) ? feed?.PatchGuide : feed?.ModGuides.FirstOrDefault(g => g.Id == _settings.Mod)?.PatchGuide;
        return guide?.Entries.FirstOrDefault(e => e.Id == "colors")?.Body(_text.Language) ?? _text.ColorText("modules.colors.help", _channel);
    }
    private async Task ApplyVanillaConfigurationAsync(UserSettings selection, Func<Task>? beforeCommit = null)
    {
        if (_game is null) throw new InvalidOperationException(_text["status.notfound"]);
        EnsureGameClosed();
        if (_channel is not null)
        {
            await ApplyConfigurationSnapshotAsync(_channel, false, selection, beforeCommit);
            return;
        }
        if (GameLanguages.Text(selection) != "en" || GameLanguages.Voice(selection) != "en")
            throw new IOException(T("Для установки выбранного языка сначала проверьте обновления.", "Check for updates before installing the selected game language."));
        if (beforeCommit is not null) await beforeCommit();
        EnsureGameClosed();
        ShowWorking(() => T("Восстанавливаю оригинальные файлы…", "Restoring original files…"));
        await new ModuleInstaller(_game.Directory).UninstallAsync(settings: EffectiveSettings.ForChannel(selection));
        InvalidateReadiness();
        _fileCheckFailed = false;
    }
    private async void Mod_Checked(object sender, RoutedEventArgs e)
    {
        if (_initializing || sender is not RadioButton { Tag: string mod } || mod == _settings.Mod) return;
        if (_busy || ConfirmationActive) { RefreshModControls(); return; }
        if (mod == GameMod.ArcaneWars && !CanSelectArcaneWars) { RefreshModControls(); return; }
        CancelBackgroundFeed();
        ModChannelSelection.SelectMod(_settings, mod);
        if (mod == GameMod.ArcaneWars && !LiveArcaneAccess && !CanUseArcaneSelection)
        {
            _settings.PinnedRelease = null;
            ModChannelSelection.SelectChannel(_settings, StoredArcaneReleases().Keys.Order().First());
        }
        _latestChannel = _feedClient.KnownChannel(_settings.Channel);
        _offeredModChannel = _feedClient.CachedGuideChannel(_settings.Channel, _settings.PinnedRelease);
        _channel = _offeredModChannel;
        _compatiblePatchUpdate = null;
        _selectedHasInstalledPatch = false; _installedRuntimeMismatch = false;
        _lastProbeDataOnly = false; _compatibilityNoticeKey = "";
        CloseCompatibilityPopup();
        _compatibilityKey = "";
        _settingsStore.Save(_settings);
        InvalidateReadiness();
        RefreshConfigurationCode();
        RefreshStatus();
        RefreshNews();
        ApplyPatchChannelLanguage();
        if (mod != GameMod.ArcaneWars) Motion.Reveal(GameMod.HasPureFixes(_channel) ? CoreModuleCard : VanillaEmptyCard);
        else foreach (var card in ArcaneComponentCards()) Motion.Reveal(card);
        // Selection is local. The normal startup/periodic/manual checks refresh releases.
        // Fetch only when this channel has never been loaded in this session.
        if (_feedClient.KnownChannel(_settings.Channel) is null)
        {
            await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
            await CheckFeedAsync(background: true);
        }
    }

    private Border[] ArcaneComponentCards() => [CoreModuleCard, ColorsModuleCard,
        OosModuleCard, IndependentHostilityCard, RoamingSpawnCard, AdditionalRoamingCard, SiegeBalanceCard, ImprovedAiCard, PowersShardsCard];

    private void RefreshPawComponentDependency()
    {
        var arcane = GameMod.IsArcaneWars(_settings);
        var enabled = GameMod.PawPatchSelected(_settings) && (arcane || GameMod.HasPureOptions(_channel));
        foreach (var card in ArcaneComponentCards().Skip(1).Append(PureLairCard))
        {
            var needsExe = !arcane || card == ColorsModuleCard || card == OosModuleCard || card == IndependentHostilityCard || card == ImprovedAiCard;
            card.Opacity = !enabled || needsExe && DataOnlyMode ? .42 : 1;
            card.ToolTip = !enabled ? T("Включите Paw's Patch, чтобы выбрать этот компонент.", "Enable Paw's Patch to select this component.")
                : needsExe && DataOnlyMode ? T("Недоступно: требуется совместимая версия EXE игры.", "Unavailable: a supported game executable is required.") : null;
        }
        ColorsToggle.IsChecked = enabled && !DataOnlyMode && _colorsAvailable && GameMod.ColorsSelected(_settings);
        IndependentHostilityToggle.IsChecked = enabled && !DataOnlyMode && _settings.IndependentHostility;
        IgnoreDesyncToggle.IsChecked = enabled && !DataOnlyMode && GameMod.DesyncSelected(_settings);
        AdditionalRoamingToggle.IsChecked = enabled && _settings.AdditionalRoamingCompanies;
        SiegeBalanceToggle.IsChecked = enabled && _settings.SiegeBalance;
        ImprovedAiToggle.IsChecked = enabled && !DataOnlyMode && GameMod.HasImprovedAi(_channel, _settings.Mod) && GameMod.ImprovedAiSelected(_settings);
        ImprovedAiToggle.IsEnabled = enabled && !_busy && !DataOnlyMode && GameMod.HasImprovedAi(_channel, _settings.Mod);
        PowersShardsToggle.IsChecked = enabled && _settings.DisablePowersAndShards;
        SelectSpawnMode(enabled ? (arcane ? _settings.RoamingSpawnMode : GameMod.PureComponents(_settings).RoamingSpawnMode) : "standard");
        if (!arcane)
        {
            var pure = GameMod.PureComponents(_settings);
            IndependentHostilityToggle.IsChecked = enabled && !DataOnlyMode && pure.IndependentHostility;
            AdditionalRoamingToggle.IsChecked = enabled && !DataOnlyMode && pure.AdditionalRoamingCompanies;
            PureLairToggle.IsChecked = enabled && !DataOnlyMode && pure.WoundedLairDefenders;
            PureLairToggle.IsEnabled = enabled && !_busy && !DataOnlyMode;
        }
        ColorsToggle.IsEnabled = enabled && !_busy && !DataOnlyMode && _colorsAvailable;
        IndependentHostilityToggle.IsEnabled = enabled && !_busy && !DataOnlyMode && CanChangeHostilityWithSelectedColors;
        IgnoreDesyncToggle.IsEnabled = enabled && !_busy && !DataOnlyMode && CanContinueWithSelectedColors;
        AdditionalRoamingToggle.IsEnabled = SiegeBalanceToggle.IsEnabled = StandardSpawnRadio.IsEnabled = X4SpawnRadio.IsEnabled = enabled && !_busy;
        X2SpawnRadio.IsEnabled = enabled && !_busy && SupportsX2(_channel);
        if (!arcane && DataOnlyMode)
            AdditionalRoamingToggle.IsEnabled = StandardSpawnRadio.IsEnabled = X2SpawnRadio.IsEnabled = X4SpawnRadio.IsEnabled = false;
        PowersShardsToggle.IsEnabled = enabled && !_busy && (PowersShardsAvailable || !_settings.DisablePowersAndShards);
    }

    private void PawPatchChanged(object sender, RoutedEventArgs e)
    {
        if (_initializing || _busy) return;
        var appearance = CaptureAppearance();
        GameMod.SetPawPatch(_settings, PawPatchToggle.IsChecked == true);
        _settingsStore.Save(_settings);
        InvalidateReadiness();
        RefreshConfigurationCode();
        RefreshStatus();
        HighlightAppearanceChanges(appearance);
    }

    private void RefreshModControls()
    {
        var previous = _initializing;
        _initializing = true;
        try
        {
            var vanilla = GameMod.IsVanilla(_settings);
            var arcane = GameMod.IsArcaneWars(_settings);
            ModulesTitleText.Text = T("Моды и компоненты", "Mods and components");
            VanillaModRadio.Content = GameMod.Name(GameMod.Vanilla);
            ImmortalsModRadio.Content = "Immortals";
            ImmortalsModRadio.ToolTip = null;
            ArcaneWarsModRadio.Content = "Arcane Wars";
            PatchChannelCard.Visibility = _activePage == "modules" ? Visibility.Visible : Visibility.Collapsed;
            VanillaModRadio.IsChecked = vanilla;
            ArcaneWarsModRadio.IsChecked = arcane;
            ImmortalsModRadio.IsChecked = _settings.Mod == GameMod.Immortals;
            VanillaModRadio.IsEnabled = ImmortalsModRadio.IsEnabled = !_busy;
            RefreshTeamAccess();
            var pureAvailable = GameMod.HasPureFixes(_channel);
            PawPatchToggle.IsChecked = GameMod.PawPatchSelected(_settings) && (arcane || pureAvailable);
            PawPatchToggle.IsEnabled = !_busy && (arcane || pureAvailable);
            System.Windows.Automation.AutomationProperties.SetName(PawPatchToggle, "Paw's Patch");
            foreach (var radio in new[] { VanillaModRadio, ImmortalsModRadio, ArcaneWarsModRadio })
                System.Windows.Automation.AutomationProperties.SetName(radio, (string)radio.Content);
            VanillaTitleText.Text = vanilla ? T("Оригинальная игра", "The original game") : "Immortals";
            VanillaDescriptionText.Text = T("Для этого режима пока нет дополнительных компонентов. Выбранный язык игры сохраняется. Настройки Arcane Wars будут восстановлены при возвращении к нему.",
                "There are no additional components for this mode yet. Your game language is retained. Your Arcane Wars choices will be restored when you return to it.");
            var modules = _activePage == "modules";
            ModSelectorCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
            PinnedComponentsHost.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
            RussianModuleCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
            SyncLanguageChoices();
            VanillaEmptyCard.Visibility = modules && !arcane && !pureAvailable ? Visibility.Visible : Visibility.Collapsed;
            MultiplayerNoteCard.Visibility = modules ? Visibility.Visible : Visibility.Collapsed;
            foreach (var card in ArcaneComponentCards()) card.Visibility = modules && arcane ? Visibility.Visible : Visibility.Collapsed;
            ImprovedAiCard.Visibility = modules && GameMod.HasImprovedAi(_channel, _settings.Mod) ? Visibility.Visible : Visibility.Collapsed;
            CoreModuleCard.Visibility = modules && (arcane || pureAvailable) ? Visibility.Visible : Visibility.Collapsed;
            if (modules && !arcane && GameMod.HasPureOptions(_channel))
                ColorsModuleCard.Visibility = OosModuleCard.Visibility = Visibility.Visible;
            bool pureBeta = modules && !arcane && GameMod.HasPureBeta(_channel);
            PureLairCard.Visibility = pureBeta && !_channel!.PureLairRecoveryBuiltIn ? Visibility.Visible : Visibility.Collapsed;
            PureLairTitleText.Text = T("Защитники логов", "Lair defenders");
            PureLairDescriptionText.Text = T("Выжившие защитники выходят в бой, не дожидаясь полного здоровья.", "Surviving defenders deploy without waiting for full health.");
            if (pureBeta) IndependentHostilityCard.Visibility = RoamingSpawnCard.Visibility = AdditionalRoamingCard.Visibility = Visibility.Visible;
            SyncPatchChannelControls();
        }
        finally { _initializing = previous; }
    }

    private string PureFixesDescription(bool partial)
    {
        if (!partial && GameMod.HasPureBeta(GuideChannel()) && GuideChannel()!.PureLairRecoveryBuiltIn)
            return T("Экономика союзника, камера, исправления рот и вылетов, настройки ботов, интерфейс наблюдателя и звуковая кнопка. Большие карты, 16 королевств и 8 команд, случайные карта и время суток. Выжившие защитники логов выходят в бой, не дожидаясь полного здоровья. Улучшения ботов, странствующие роты и вражда независимых семейств настраиваются отдельно.",
                "Allied economy, camera, company and crash fixes, bot settings, observer interface and sound button. Larger maps, 16 kingdoms and 8 teams, random map and time of day. Surviving lair defenders deploy without waiting for full health. Bot improvements, roaming companies and independent family hostility have separate settings.");
        if (!partial && GameMod.HasPureBeta(GuideChannel()))
            return T("Экономика союзника, более далёкая камера, исправления рот и вылетов, общие настройки ботов, интерфейс наблюдателя и звуковая кнопка. Добавлены большие карты, 16 королевств и 8 команд, случайные карта и время суток. Улучшения ботов, защитники логов, странствующие роты и вражда независимых семейств настраиваются отдельно.",
                "Allied economy, extended camera, company and crash fixes, shared bot settings, observer interface and sound button. Adds larger maps, 16 kingdoms and 8 teams, random map and time of day. Bot improvements, lair defenders, roaming companies and independent family hostility have separate settings.");
        // A locally installed package can retain an older manifest. Describe the
        // selected channel, like the guide does; an explicit release pin still
        // resolves to its historical documentation.
        var entries = GuideChannel()?.ModGuides.FirstOrDefault(g => g.Id == _settings.Mod)?.PatchGuide?.Entries;
        var hasDvorak = entries?.Any(e => e.Id == "dvorak") == true;
        var hasFastTransfer = entries?.Any(e => e.Id == "fast-save-transfer") == true;
        var text = (partial ? T("Работают:\n", "Available:\n") : "")
            + T("• Исправляет отображение цветов на значках рот.", "• Fixes colors on company badges.");
        if (hasDvorak) text += T("\n• Камера на WASD и стрелках, союзная метка на F в профиле Dvorak.",
            "\n• WASD and arrow-key camera controls, with the F allied marker in the Dvorak profile.");
        text += partial
            ? T("\n\nНедоступны для этой версии игры:\n• Отображение «−0» как «0» в лимите рот.\n• Исправление неинициализированного параметра рельефа.",
                "\n\nUnavailable for this game version:\n• Displaying negative zero as zero in the company limit.\n• Fix for an uninitialized terrain parameter.")
            : T("\n• Показывает «0» вместо «−0» в лимите рот.\n• Исправляет неинициализированный параметр рельефа при создании карты.",
                "\n• Displays zero instead of negative zero in the company limit.\n• Fixes an uninitialized terrain parameter during map generation.");
        if (hasFastTransfer) text += T("\n• Ускоренная штатная передача сохранений участникам сетевого лобби.",
            "\n• Faster native saved-game transfers to multiplayer lobby participants.");
        if (GameMod.HasPureOptions(GuideChannel()))
            text += T("\n\nДополнительные переключатели: 39 цветов с компактным выбором и игнорирование рассинхронов. При пропуске рассинхрона сохраняется диагностический журнал первой ошибки в матче. Требуется совместимый EXE игры. При выключении Paw's Patch оба переключателя отключаются.",
                "\n\nOptional switches: 39 colors with a compact picker and Ignore desyncs. Continuing after a desync saves a diagnostic log of the first error in the match. A supported game executable is required. Turning Paw's Patch off disables both switches.");
        if (!partial) text += T("\n\nПравила и баланс выбранного режима сохраняются. По умолчанию используется стандартная обработка рассинхронов.",
            "\n\nThe selected mode's rules and balance are preserved. Standard desync handling is used by default.");
        return text;
    }
}
