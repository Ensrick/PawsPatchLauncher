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
    private string? _customModsError, _customModsNotice;
    private readonly Dictionary<string, HashSet<string>?> _customModResources = new(StringComparer.OrdinalIgnoreCase);
    private bool _customModsDirty, _customModsBusy;
    private string _customModsGame = GameMod.Vanilla;
    private Func<string?>? _chooseRwdMod = null;
    private string? ChooseRwdMod()
    {
        var dialog = new OpenFileDialog { Filter = "Kohan II mods (*.rwd)|*.rwd", CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void CustomModsNav_Click(object sender, RoutedEventArgs e) => SetActivePage("custom-mods");
    private async void CustomModsSave_Click(object sender, RoutedEventArgs e) => await SaveCustomModsAsync();

    private void ApplyCustomModsLanguage()
    {
        CustomModsNav.Content = T("Моды", "Mods");
        CustomModsTitle.Text = T("Пользовательские моды", "Custom mods");
        CustomModsHint.Text = T("Добавляйте RWD-архивы для выбранной игры. Нижний включённый мод имеет приоритет при совпадении файлов.",
            "Add RWD archives for the selected game. Enabled mods lower in the list take priority when files overlap.");
        CustomModsComponents.Content = T("Игра и компоненты", "Game and components");
        CustomModsSave.Content = T("Применить моды", "Apply mods");
        if (_activePage == "custom-mods") BuildCustomMods();
    }

    private void OpenCustomMods()
    {
        _customModsState = null; _customModsDraft = new(); _customModsError = _customModsNotice = null; _customModsDirty = false;
        _customModResources.Clear();
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
        RefreshCustomModsState(IsGameRunning());
    }

    private List<CustomRwdMod> CurrentCustomMods => _customModsDraft.TryGetValue(_customModsGame, out var list) ? list : [];
    private bool CanEditCustomMods => _game is not null && _customModsState is not null && !_settingsPending
        && _customModsState.AppliedSettings?.Mod == _customModsGame;

    private HashSet<string>? CustomModResources(CustomRwdMod mod)
    {
        if (_game is null) return null;
        if (_customModResources.TryGetValue(mod.Sha256, out var known)) return known;
        HashSet<string>? names = null;
        try { names = new CustomRwdMods(_game.Directory).Resources(mod.Sha256); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException) { ActivityStore.Log(ex); }
        return _customModResources[mod.Sha256] = names;
    }

    private void CustomModsUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_customModsBusy || _customModsState is null) return;
        _customModsDraft = CustomRwdMods.Copy(_customModsState.CustomMods);
        CustomModsChanged();
    }

    private void BuildCustomMods()
    {
        CustomModsGameLabel.Text = GameMod.Name(_customModsGame, _text.Language == "ru")
            + (_customModsState?.AppliedSettings?.DataOnly == true
                ? " · " + T("Только файловые изменения", "File changes only")
                    + (_installedGameVersion.Length > 0 ? " · Kohan II " + _installedGameVersion : "")
                : "");
        CustomModsAdd.Content = T("Добавить RWD…", "Add RWD…");
        CustomModsUndo.Content = T("Отменить изменения", "Undo changes");
        CustomModsBody.Children.Clear();
        var list = CurrentCustomMods;
        if (list.Count == 0)
        {
            var empty = GameSettingLabel(T("Пока нет пользовательских модов. Добавьте архив .rwd — исходный файл останется на месте.",
                "No custom mods yet. Add an .rwd archive; the original file will stay where it is."));
            empty.Margin = new Thickness(8, 24, 8, 24);
            CustomModsBody.Children.Add(empty);
        }
        var resources = list.Select(m => m.Enabled ? (IReadOnlySet<string>?)CustomModResources(m) : null).ToList();
        var overlaps = CustomRwdMods.Overrides(resources);
        var position = 0;
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
            var name = GameSettingLabel((mod.Enabled ? (++position).ToString() : "–") + " · " + mod.Name); name.FontWeight = FontWeights.SemiBold;
            description.Children.Add(name);
            description.Children.Add(new TextBlock { Text = T("Файлов: ", "Files: ") + $"{mod.Files:N0} · " + FormatBytes(mod.Size),
                FontSize = 11, Foreground = (Brush)FindResource("TextMutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
            if (mod.Enabled && resources[i] is null)
                description.Children.Add(GameSettingLabel(T("Не удалось проверить пересечения файлов.", "Could not check file overlaps.")));
            else if (overlaps[i].Count > 0)
            {
                var overlap = GameSettingLabel(T("Совпадения с модами выше: ", "Matching files in earlier mods: ")
                    + string.Join(", ", overlaps[i].Select(o => $"{list[o.Earlier].Name} ({o.Files:N0})")));
                overlap.FontSize = 11; overlap.Margin = new Thickness(0, 4, 0, 0);
                overlap.Foreground = (Brush)FindResource("GoldBrightBrush");
                // Advisory only: normalized names are not proof of identical game content.
                var paths = resources[i]!.Where(p => overlaps[i].Any(o => resources[o.Earlier]!.Contains(p)))
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                overlap.ToolTip = string.Join("\n", paths.Take(20)) + (paths.Count > 20 ? "\n…" : "");
                description.Children.Add(overlap);
            }
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
            Action("↻", T("Заменить архив…", "Replace archive…"), async () => await ImportCustomModAsync(index));
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
    private void CustomModsChanged(bool keepNotice = false)
    {
        _customModsError = null;
        if (!keepNotice) _customModsNotice = null;
        _customModsDirty = !CurrentCustomMods.SequenceEqual(_customModsState?.CustomMods.GetValueOrDefault(_customModsGame) ?? []);
        BuildCustomMods(); RefreshGameLaunchState();
    }

    private void RefreshCustomModsState(bool running)
    {
        if (CustomModsPanel is null || _activePage != "custom-mods") return;
        CustomModsBody.IsEnabled = CanEditCustomMods && !running && !_customModsBusy && !_busy && !_launchStarting;
        // At capacity the picker still permits replacing an existing archive.
        CustomModsAdd.IsEnabled = CustomModsBody.IsEnabled;
        CustomModsUndo.IsEnabled = _customModsDirty && !_customModsBusy;
        CustomModsComponents.IsEnabled = !_customModsBusy && !_busy && !_launchStarting;
        CustomModsStatus.Text = _customModsError ?? (_customModsBusy ? T("Проверяю и применяю моды…", "Checking and applying mods…")
            : running ? T("Закройте игру, чтобы сохранить изменения.", "Close the game to save changes.")
            : !CanEditCustomMods ? T("Сначала примените выбранную игру и компоненты на вкладке «Компоненты».", "First apply the selected game and components on the Components page.")
            : _customModsNotice ?? (_customModsDirty ? T("Изменения ещё не применены.", "Changes are not applied yet.") : ""));
        CustomModsStatus.Visibility = CustomModsStatus.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        CustomModsSave.IsEnabled = CanEditCustomMods && _customModsDirty && !running && !_customModsBusy && !_busy && !_launchStarting;
    }

    private async void CustomModsAdd_Click(object sender, RoutedEventArgs e)
        => await ImportCustomModAsync();

    private async Task ImportCustomModAsync(int? replaceIndex = null)
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
            var sameName = list.Select((m, index) => (m, index)).Where(p => p.m.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            var target = replaceIndex ?? (sameName.Count == 1 ? sameName[0].index : -1);
            if (list.Any(m => m.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                _customModsError = T("Этот архив уже добавлен.", "This archive is already in the list.");
            else if (target >= 0)
            {
                list[target] = entry with { Enabled = list[target].Enabled };
                _customModsNotice = T("Архив заменён в списке. Примените изменения или отмените их.", "Archive replaced in the list. Apply or undo the changes.");
                CustomModsChanged(keepNotice: true);
            }
            else if (list.Count >= CustomRwdMods.MaximumMods)
                _customModsError = T("Можно добавить не более 32 модов. Удалите один или замените его архив.", "You can add up to 32 mods. Remove one or replace its archive.");
            else { list.Add(entry); CustomModsChanged(); }
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось добавить архив. Нужен исправный RWD для Kohan II размером до 2 ГБ.", "Could not add the archive. Choose a valid Kohan II RWD up to 2 GB."); }
        finally { _customModsBusy = false; RefreshGameSettingsState(IsGameRunning()); }
    }

    private async Task SaveCustomModsAsync()
    {
        RefreshGameSettingsState(IsGameRunning());
        if (!CustomModsSave.IsEnabled || _game is null || _customModsState?.AppliedSettings is null) return;
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
            _customModsDraft = CustomRwdMods.Copy(_customModsState.CustomMods);
            _customModsNotice = T("Пользовательские моды применены.", "Custom mods applied.");
            BuildCustomMods();
            RefreshStatus();
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось применить моды. Проверьте файлы и заново откройте это окно.", "Could not apply mods. Check the files and reopen this window."); }
        finally { _customModsBusy = false; SetBusy(false); RefreshGameSettingsState(IsGameRunning()); }
    }
}
