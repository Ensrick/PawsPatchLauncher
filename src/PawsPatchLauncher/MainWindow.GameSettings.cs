using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    // Deliberately separate from UserSettings/configuration codes and friend sharing.
    private Func<string> _gameUserSettingsPath = () => ActivityStore.IsSmokeTest
        ? Path.Combine(ActivityStore.Root, "game-settings", "UVars.tgi") : GameUserSettings.UserPath;
    private GameUserSettings? _gameUserSettings;
    private readonly Dictionary<string, string> _gameSettingsEdits = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Control> _gameSettingsInputs = new(StringComparer.Ordinal);
    private bool _buildingGameSettings;
    private bool _gameSettingsPreviousEnabled;
    private string? _gameSettingsError;
    private sealed record UnchangedResolution(string Label);

    private void ApplyGameSettingsLanguage()
    {
        GameSettingsButton.ToolTip = GameSettingsTitle.Text = T("Настройки игры", "Game settings");
        AutomationProperties.SetName(GameSettingsButton, GameSettingsTitle.Text);
        GameOptionsTab.Content = T("Настройки игры", "Game settings");
        CustomModsTab.Content = T("Пользовательские моды", "Custom mods");
        GameSettingsClose.ToolTip = T("Закрыть", "Close");
        AutomationProperties.SetName(GameSettingsClose, (string)GameSettingsClose.ToolTip);
        GameSettingsCancel.Content = T("Отмена", "Cancel");
        GameSettingsSave.Content = T("Сохранить", "Save");
        RefreshGameSettingsTab();
    }

    private void GameSettings_Click(object sender, RoutedEventArgs e) => OpenGameSettings();

    private void OpenGameSettings()
    {
        if (_busy || _launchStarting || ConfirmationActive || ModNoticeOverlay.IsVisible || GameSettingsOverlay.IsVisible) return;
        _gameSettingsEdits.Clear(); _gameSettingsInputs.Clear(); _gameSettingsError = null;
        _gameUserSettings = null; GameSettingsBody.Children.Clear();
        ApplyGameSettingsLanguage();
        _buildingGameSettings = true;
        try
        {
            _gameUserSettings = GameUserSettings.Load(_gameUserSettingsPath());
            BuildGameSettings();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            _gameSettingsError = T("Не удалось прочитать настройки игры. Проверьте доступ к UVars.tgi.",
                "Could not read game settings. Check access to UVars.tgi.");
        }
        finally { _buildingGameSettings = false; }
        OpenCustomMods();
        GameOptionsTab.IsChecked = true;
        RefreshGameSettingsTab();
        _gameSettingsPreviousEnabled = MainBody.IsEnabled;
        MainBody.IsEnabled = false;
        GameSettingsOverlay.Visibility = Visibility.Visible;
        RefreshGameSettingsState(IsGameRunning());
        GameSettingsClose.Focus();
    }

    private TextBlock GameSettingLabel(string text, bool heading = false) => new()
    {
        Text = text, TextWrapping = TextWrapping.Wrap,
        FontSize = heading ? 16 : 13, FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = (Brush)FindResource(heading ? "GoldBrightBrush" : "TextMainBrush"),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void GameSettingsSection(string title)
    {
        var label = GameSettingLabel(title, true);
        label.Margin = new Thickness(0, GameSettingsBody.Children.Count == 0 ? 0 : 18, 0, 10);
        GameSettingsBody.Children.Add(label);
    }

    private TextBox GameSettingNumber(string key, string label, string? value, int maxLength = 8)
    {
        var box = new TextBox
        {
            Style = (Style)FindResource("ConfigurationInput"), Text = value ?? "",
            MaxLength = maxLength, Margin = new Thickness(0), TextWrapping = TextWrapping.NoWrap, MinWidth = 90
        };
        AutomationProperties.SetName(box, label);
        box.ToolTip = label;
        _gameSettingsInputs[key] = box;
        return box;
    }

    private void BuildGameSettings()
    {
        var file = _gameUserSettings!;
        var defaultText = T("По умолчанию", "Default");
        GameSettingsSection(T("Экран", "Display"));
        var screen = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        screen.ColumnDefinitions.Add(new ColumnDefinition());
        screen.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        screen.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(135) });
        var resolutionPanel = new StackPanel();
        resolutionPanel.Children.Add(GameSettingLabel(T("Разрешение", "Resolution")));
        var modes = new ComboBox { Style = (Style)FindResource("ReleaseCombo"), DisplayMemberPath = "Label", Margin = new Thickness(0, 6, 0, 0), MinWidth = 170 };
        AutomationProperties.SetName(modes, T("Разрешение", "Resolution"));
        _gameSettingsInputs["DisplayModes"] = modes;
        GameResolution? current = file.Number("ResolutionX") is { } x && file.Number("ResolutionY") is { } y
            && x == Math.Truncate(x) && y == Math.Truncate(y)
            && x >= 800 && x <= 16384 && y >= 600 && y <= 16384 ? new((int)x, (int)y) : null;
        var choices = GameResolution.WidescreenChoices;
        // Preserve an existing nonstandard size (or absence) until a preset is explicitly selected.
        UnchangedResolution? unchanged = null;
        if (current is null || !choices.Contains(current))
        {
            unchanged = new UnchangedResolution(current?.Label ?? defaultText);
            modes.Items.Add(unchanged);
        }
        foreach (var mode in choices) modes.Items.Add(mode);
        modes.SelectedItem = (object?)unchanged ?? current;
        resolutionPanel.Children.Add(modes);
        screen.Children.Add(resolutionPanel);

        var fpsPanel = new StackPanel(); Grid.SetColumn(fpsPanel, 2);
        fpsPanel.Children.Add(GameSettingLabel(T("Лимит FPS", "FPS limit")));
        var fps = GameSettingNumber("FrameRateLimit", T("Лимит FPS: 1–1000", "FPS limit: 1–1000"),
            file.Number("FrameRateLimit")?.ToString("0.###", CultureInfo.InvariantCulture));
        fps.Margin = new Thickness(0, 6, 0, 0);
        fpsPanel.Children.Add(fps); screen.Children.Add(fpsPanel);
        GameSettingsBody.Children.Add(screen);
        var fpsDefault = GameSettingLabel(defaultText);
        fpsDefault.Style = (Style)FindResource("MetadataText");
        fpsDefault.Visibility = file.Value("FrameRateLimit") is null ? Visibility.Visible : Visibility.Collapsed;
        fpsPanel.Children.Add(fpsDefault);
        fps.TextChanged += (_, _) =>
        {
            if (_buildingGameSettings) return;
            fpsDefault.Visibility = Visibility.Collapsed;
            SetGameSetting("FrameRateLimit", fps.Text.Trim().Replace(',', '.'));
        };

        modes.SelectionChanged += (_, _) =>
        {
            if (_buildingGameSettings) return;
            if (modes.SelectedItem is GameResolution chosen && chosen != current)
            {
                _gameSettingsEdits["ResolutionX"] = chosen.Width.ToString(CultureInfo.InvariantCulture);
                _gameSettingsEdits["ResolutionY"] = chosen.Height.ToString(CultureInfo.InvariantCulture);
            }
            else { _gameSettingsEdits.Remove("ResolutionX"); _gameSettingsEdits.Remove("ResolutionY"); }
            _gameSettingsError = null; RefreshGameSettingsState(IsGameRunning());
        };

        GameSettingsSection(T("Звук", "Sound"));
        foreach (var (key, title) in new[]
        {
            ("AudioMainVolume", T("Общая громкость", "Master volume")),
            ("Audio2DVolume", T("Интерфейс", "Interface sounds")),
            ("Audio3DVolume", T("Звуки мира", "World sounds")),
            ("AudioSpeechVolume", T("Речь", "Speech")),
            ("AudioMusicVolume", T("Музыка", "Music"))
        })
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            row.Children.Add(GameSettingLabel(title));
            var initial = file.Number(key);
            var slider = new DragAnywhereSlider
            {
                Style = (Style)FindResource("NotificationVolumeSliderStyle"), Minimum = 0, Maximum = 100,
                Value = Math.Clamp((initial ?? 0.8) * 100, 0, 100), TickFrequency = 1,
                IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10, Margin = new Thickness(8, 0, 12, 0)
            };
            AutomationProperties.SetName(slider, title); Grid.SetColumn(slider, 1); row.Children.Add(slider);
            var value = GameSettingLabel(initial is null ? defaultText : Math.Round(initial.Value * 100) + "%");
            value.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(value, 2); row.Children.Add(value);
            _gameSettingsInputs[key] = slider;
            slider.ValueChanged += (_, _) =>
            {
                if (_buildingGameSettings) return;
                value.Text = Math.Round(slider.Value) + "%";
                SetGameSetting(key, (Math.Round(slider.Value) / 100).ToString("0.00", CultureInfo.InvariantCulture));
            };
            GameSettingsBody.Children.Add(row);
        }
        GameSettingsSection(T("Интерфейс", "Interface"));
        foreach (var (key, title) in new[]
        {
            ("ViewShowElapsedGameTime", T("Показывать таймер матча", "Show match timer")),
            ("MinimapColorByKingdom", T("Цвета игроков на миникарте", "Player colors on the minimap")),
            // Stock 1.3.72 setting, off by default: on a non-4:3 screen the HUD keeps its proportions instead of
            // stretching sideways (minimap, control panel art); the world still fills the screen.
            ("WidescreenUniformUI", T("Не растягивать интерфейс на широком экране", "Don't stretch the interface on widescreen"))
        })
        {
            var check = new CheckBox { Content = title, IsChecked = file.Flag(key), Margin = new Thickness(0, 2, 0, 8) };
            AutomationProperties.SetName(check, title); _gameSettingsInputs[key] = check;
            check.Checked += (_, _) => SetGameSetting(key, "true");
            check.Unchecked += (_, _) => SetGameSetting(key, "false");
            GameSettingsBody.Children.Add(check);
        }
    }

    private void SetGameSetting(string key, string value)
    {
        if (_buildingGameSettings) return;
        if (_gameUserSettings!.Value(key) == value
            || double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && _gameUserSettings.Number(key) == number)
            _gameSettingsEdits.Remove(key);
        else _gameSettingsEdits[key] = value;
        _gameSettingsError = null;
        RefreshGameSettingsState(IsGameRunning());
    }

    private void RefreshGameSettingsState(bool running)
    {
        if (GameSettingsButton is null) return;
        GameSettingsButton.IsEnabled = !_busy && !_launchStarting;
        if (GameSettingsOverlay.Visibility != Visibility.Visible) return;
        if (CustomModsTab.IsChecked == true) { RefreshCustomModsState(running); return; }
        string? invalid = null;
        try { foreach (var (key, value) in _gameSettingsEdits) GameUserSettings.Validate(key, value); }
        catch (ArgumentException)
        {
            invalid = T("Лимит FPS: 1–1000", "FPS limit: 1–1000");
        }
        GameSettingsStatus.Text = _gameSettingsError ?? (running
            ? T("Закройте игру, чтобы сохранить изменения.", "Close the game to save changes.") : invalid) ?? "";
        GameSettingsStatus.Visibility = GameSettingsStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        GameSettingsSave.IsEnabled = _gameUserSettings is not null && _gameSettingsEdits.Count > 0
            && !running && !_busy && !_launchStarting && invalid is null && _gameSettingsError is null;
    }

    private async void GameSettingsSave_Click(object sender, RoutedEventArgs e)
    {
        if (CustomModsTab.IsChecked == true) { await SaveCustomModsAsync(); return; }
        RefreshGameSettingsState(IsGameRunning());
        if (!GameSettingsSave.IsEnabled || _gameUserSettings is null) return;
        try
        {
            _gameUserSettings.Save(_gameSettingsEdits, IsGameRunning);
            if (_customModsDirty)
            {
                _gameSettingsEdits.Clear();
                _gameUserSettings = GameUserSettings.Load(_gameUserSettingsPath());
                CustomModsTab.IsChecked = true;
            }
            else CloseGameSettings();
            ShowResult(() => T("Настройки игры сохранены.", "Game settings saved."));
        }
        catch (GameUserSettingsChangedException)
        {
            _gameSettingsError = T("Файл настроек изменился. Откройте это окно заново и повторите изменения.",
                "The settings file changed. Reopen this window and make your changes again.");
        }
        catch (GameUserSettingsRunningException)
        {
            _gameSettingsError = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException)
        {
            _gameSettingsError = T("Не удалось сохранить настройки. Проверьте доступ к UVars.tgi и свободное место на диске.",
                "Could not save settings. Check access to UVars.tgi and free disk space.");
        }
        RefreshGameSettingsState(IsGameRunning());
    }

    private void CloseGameSettings()
    {
        if (_customModsBusy) return;
        GameSettingsOverlay.Visibility = Visibility.Collapsed;
        MainBody.IsEnabled = _gameSettingsPreviousEnabled;
        _gameUserSettings = null; _gameSettingsEdits.Clear(); _gameSettingsInputs.Clear();
        GameSettingsBody.Children.Clear(); GameSettingsButton.Focus();
        _customModsDirty = false; _customModsState = null; CustomModsBody.Children.Clear();
    }

    private void GameSettingsCancel_Click(object sender, RoutedEventArgs e) => CloseGameSettings();
    private void GameSettings_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, GameSettingsOverlay)) { CloseGameSettings(); e.Handled = true; }
    }
    private void GameSettings_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_gameSettingsInputs.Values.OfType<ComboBox>().Any(c => c.IsDropDownOpen))
        { CloseGameSettings(); e.Handled = true; }
    }
}
