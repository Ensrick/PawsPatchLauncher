using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private InstallState? _customModsState;
    private Dictionary<string, List<CustomRwdMod>> _customModsDraft = new();
    private string? _customModsError;
    private bool _customModsDirty, _customModsBusy;
    private string _customModsGame = GameMod.Vanilla;
    private Func<string?>? _chooseRwdMod = null;
    private string? ChooseRwdMod()
    {
        var dialog = new OpenFileDialog { Filter = "Kohan II mods (*.rwd)|*.rwd", CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void GameSettingsTab_Checked(object sender, RoutedEventArgs e)
    {
        if (CustomModsPanel is null) return; // XAML initialization raises Checked before the panel exists.
        RefreshGameSettingsTab();
        RefreshGameSettingsState(IsGameRunning());
    }

    private void RefreshGameSettingsTab()
    {
        var mods = CustomModsTab.IsChecked == true;
        GameSettingsScroll.Visibility = mods ? Visibility.Collapsed : Visibility.Visible;
        CustomModsPanel.Visibility = mods ? Visibility.Visible : Visibility.Collapsed;
        GameSettingsHint.Text = mods
            ? T("Добавляйте RWD-архивы для выбранной игры. Нижний включённый мод имеет приоритет при совпадении файлов.",
                "Add RWD archives for the selected game. Enabled mods lower in the list take priority when files overlap.")
            : T("Общие для всех модов на этом компьютере. Применяются при следующем запуске игры.",
                "Shared by all mods on this computer. Applied when the game next starts.");
        GameSettingsSave.Content = mods ? T("Применить моды", "Apply mods") : T("Сохранить", "Save");
    }

    private void OpenCustomMods()
    {
        _customModsState = null; _customModsDraft = new(); _customModsError = null; _customModsDirty = false;
        _customModsGame = _settings.Mod;
        try
        {
            if (_game is not null)
            {
                _customModsState = new ModuleInstaller(_game.Directory).LoadState();
                _customModsDraft = CustomRwdMods.Copy(_customModsState.CustomMods);
            }
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось прочитать список модов.", "Could not read the mod list."); }
        BuildCustomMods();
    }

    private List<CustomRwdMod> CurrentCustomMods => _customModsDraft.TryGetValue(_customModsGame, out var list) ? list : [];
    private bool CanEditCustomMods => _game is not null && _customModsState is not null && !_settingsPending
        && _customModsState.AppliedSettings?.Mod == _customModsGame;

    private void BuildCustomMods()
    {
        CustomModsGameLabel.Text = GameMod.Name(_customModsGame, _text.Language == "ru");
        CustomModsAdd.Content = T("Добавить RWD…", "Add RWD…");
        CustomModsBody.Children.Clear();
        var list = CurrentCustomMods;
        if (list.Count == 0)
        {
            var empty = GameSettingLabel(T("Пока нет пользовательских модов. Добавьте архив .rwd — исходный файл останется на месте.",
                "No custom mods yet. Add an .rwd archive; the original file will stay where it is."));
            empty.Margin = new Thickness(8, 24, 8, 24);
            CustomModsBody.Children.Add(empty);
        }
        for (int i = 0; i < list.Count; i++)
        {
            var index = i; var mod = list[i];
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var check = new CheckBox { IsChecked = mod.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
            AutomationProperties.SetName(check, mod.Name);
            check.Checked += (_, _) => ChangeCustomMod(index, m => m with { Enabled = true });
            check.Unchecked += (_, _) => ChangeCustomMod(index, m => m with { Enabled = false });
            grid.Children.Add(check);
            var description = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = GameSettingLabel(mod.Name); name.FontWeight = FontWeights.SemiBold;
            description.Children.Add(name);
            description.Children.Add(new TextBlock { Text = T("Файлов: ", "Files: ") + $"{mod.Files:N0} · " + FormatBytes(mod.Size),
                FontSize = 11, Foreground = (Brush)FindResource("TextMutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
            Grid.SetColumn(description, 1); grid.Children.Add(description);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button Action(string label, string tip, Action action, bool enabled = true)
            {
                var button = new Button { Content = label, ToolTip = tip, Width = 30, Height = 30, Padding = new Thickness(0),
                    Margin = new Thickness(3, 0, 0, 0), Style = (Style)FindResource("GhostButton"), IsEnabled = enabled };
                AutomationProperties.SetName(button, tip + ": " + mod.Name);
                button.Click += (_, _) => action(); actions.Children.Add(button); return button;
            }
            Action("↑", T("Выше", "Move up"), () => MoveCustomMod(index, -1), i > 0);
            Action("↓", T("Ниже", "Move down"), () => MoveCustomMod(index, 1), i + 1 < list.Count);
            Action("×", T("Убрать из списка", "Remove from list"), () => { list.RemoveAt(index); CustomModsChanged(); });
            Grid.SetColumn(actions, 2); grid.Children.Add(actions);
            CustomModsBody.Children.Add(new Border { Child = grid, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8),
                CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromRgb(23, 47, 70)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(59, 84, 109)), BorderThickness = new Thickness(1) });
        }
        var hint = GameSettingLabel(T("Для сетевой игры используйте совместимые моды и одинаковый порядок загрузки. Выбор сохраняется только на этом компьютере.",
            "For multiplayer, use compatible mods in the same load order. This selection is saved only on this computer."));
        hint.FontSize = 12; hint.Margin = new Thickness(0, 10, 0, 0); hint.Foreground = (Brush)FindResource("TextMutedBrush");
        CustomModsBody.Children.Add(hint);
    }

    private void ChangeCustomMod(int index, Func<CustomRwdMod, CustomRwdMod> change)
    { CurrentCustomMods[index] = change(CurrentCustomMods[index]); CustomModsChanged(); }
    private void MoveCustomMod(int index, int direction)
    {
        var list = CurrentCustomMods;
        (list[index], list[index + direction]) = (list[index + direction], list[index]); CustomModsChanged();
    }
    private void CustomModsChanged()
    {
        _customModsError = null;
        _customModsDirty = !CurrentCustomMods.SequenceEqual(_customModsState?.CustomMods.GetValueOrDefault(_customModsGame) ?? []);
        BuildCustomMods(); RefreshGameSettingsState(IsGameRunning());
    }

    private void RefreshCustomModsState(bool running)
    {
        CustomModsBody.IsEnabled = CanEditCustomMods && !running && !_customModsBusy;
        CustomModsAdd.IsEnabled = CustomModsBody.IsEnabled && CurrentCustomMods.Count < CustomRwdMods.MaximumMods;
        GameOptionsTab.IsEnabled = CustomModsTab.IsEnabled = GameSettingsClose.IsEnabled = GameSettingsCancel.IsEnabled = !_customModsBusy;
        GameSettingsStatus.Text = _customModsError ?? (_customModsBusy ? T("Проверяю и применяю моды…", "Checking and applying mods…")
            : running ? T("Закройте игру, чтобы сохранить изменения.", "Close the game to save changes.")
            : !CanEditCustomMods ? T("Сначала примените выбранную игру и компоненты на вкладке «Компоненты».", "First apply the selected game and components on the Components page.") : "");
        GameSettingsStatus.Visibility = GameSettingsStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        GameSettingsSave.IsEnabled = CanEditCustomMods && _customModsDirty && !running && !_customModsBusy && !_busy && !_launchStarting;
    }

    private async void CustomModsAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_customModsBusy || !CustomModsAdd.IsEnabled || _game is null) return;
        _customModsBusy = true; _customModsError = null; RefreshGameSettingsState(IsGameRunning());
        try
        {
            var path = _chooseRwdMod is { } choose ? choose() : ChooseRwdMod();
            if (string.IsNullOrWhiteSpace(path)) return;
            var service = new CustomRwdMods(_game.Directory);
            var entry = await Task.Run(() => service.Import(path));
            if (!_customModsDraft.TryGetValue(_customModsGame, out var list)) _customModsDraft[_customModsGame] = list = [];
            if (list.Any(m => m.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                _customModsError = T("Этот архив уже добавлен.", "This archive is already in the list.");
            else { list.Add(entry); CustomModsChanged(); }
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось добавить архив. Нужен исправный RWD для Kohan II размером до 2 ГБ.", "Could not add the archive. Choose a valid Kohan II RWD up to 2 GB."); }
        finally { _customModsBusy = false; RefreshGameSettingsState(IsGameRunning()); }
    }

    private async Task SaveCustomModsAsync()
    {
        RefreshGameSettingsState(IsGameRunning());
        if (!GameSettingsSave.IsEnabled || _game is null || _customModsState?.AppliedSettings is null) return;
        var state = _customModsState; var game = _game.Directory; var draft = CustomRwdMods.Copy(_customModsDraft);
        _customModsBusy = true; _customModsError = null; RefreshGameSettingsState(IsGameRunning());
        SetBusy(true, T("Проверяю и применяю моды…", "Checking and applying mods…"));
        try
        {
            EnsureGameClosed();
            await Task.Run(() => new ModuleInstaller(game).ReconcileAsync(state.Modules,
                settings: state.AppliedSettings, releaseId: state.ReleaseId, gameRequirement: state.GameRequirement,
                baseGameSha256: state.BaseGameSha256, customMods: draft,
                expectedState: JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), ensureGameClosed: EnsureGameClosed));
            _customModsDirty = false; _customModsState = new ModuleInstaller(game).LoadState();
            InvalidateReadiness(); _fileCheckFailed = false;
            _customModsBusy = false;
            RefreshCustomModsState(IsGameRunning());
            if (_gameSettingsEdits.Count > 0) GameOptionsTab.IsChecked = true;
            else CloseGameSettings();
            RefreshStatus();
            ShowToast(() => T("Пользовательские моды применены.", "Custom mods applied."));
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось применить моды. Проверьте файлы и заново откройте это окно.", "Could not apply mods. Check the files and reopen this window."); }
        finally { _customModsBusy = false; SetBusy(false); RefreshGameSettingsState(IsGameRunning()); }
    }
}
