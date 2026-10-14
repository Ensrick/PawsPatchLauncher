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
    private bool _customModsDirty, _customModsBusy;
    private string _customModsGame = GameMod.Vanilla;
    private readonly Dictionary<string, HashSet<string>?> _customModResources = new(StringComparer.OrdinalIgnoreCase);
    private Func<string?>? _chooseRwdMod = null;
    private string? ChooseRwdMod()
    {
        var dialog = new OpenFileDialog { Filter = "Kohan II mods (*.rwd)|*.rwd", CheckFileExists = true };
        return dialog.ShowDialog(this) == true ? dialog.FileName : null;
    }

    private void ApplyModManagerLanguage()
    {
        ModManagerNav.Content = ModManagerTitleText.Text = T("Менеджер модов", "Mod manager");
        ModManagerDescriptionText.Text = T("Подключайте свои RWD-моды поверх выбранной игры. Моды загружаются сверху вниз: если два мода меняют один файл, побеждает нижний.",
            "Load your own RWD mods on top of the selected game. Mods load from top to bottom; when two mods change the same file, the lower one wins.");
        ModManagerOrderTitleText.Text = T("Порядок загрузки", "Load order");
        CustomModsAdd.Content = T("Добавить RWD…", "Add RWD…");
        ModManagerApply.Content = T("Применить моды", "Apply mods");
        ModManagerRevert.Content = T("Отменить", "Undo");
        ModManagerComponentsButton.Content = _text["nav.modules"];
        ModManagerMultiplayerText.Text = T("Для сетевой игры используйте совместимые моды и одинаковый порядок загрузки. Выбор сохраняется только на этом компьютере.",
            "For multiplayer, use compatible mods in the same load order. This selection is saved only on this computer.");
        ApplyModManagerGamesLanguage();
        if (_activePage == "modmanager") { BuildBaseGames(); BuildCustomMods(); }
    }

    private void ModManagerNav_Click(object sender, RoutedEventArgs e) => SetActivePage("modmanager");
    private void ModManagerComponents_Click(object sender, RoutedEventArgs e) => SetActivePage("modules");

    // Unapplied edits survive page changes while the same game stays selected.
    private void OpenCustomMods()
    {
        if (_customModsBusy) return;
        if (_customModsDirty && _customModsState is not null && _customModsGame == _settings.Mod) { BuildBaseGames(); BuildCustomMods(); RefreshCustomModsState(IsGameRunning()); return; }
        ReloadCustomMods();
    }

    private void ReloadCustomMods()
    {
        _customModsState = null; _customModsDraft = new(); _customModsError = _customModsNotice = null; _customModsDirty = false;
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
        BuildBaseGames();
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

    // A null order marks the base game row, which shows the game icon instead of a load position.
    private Border CustomModRow(string? order, bool active, UIElement? toggle, string title, string details, string? overrides, StackPanel? actions)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var badge = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(15), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(active ? Color.FromRgb(59, 47, 22) : Color.FromRgb(27, 44, 64)),
            BorderBrush = active ? (Brush)FindResource("GoldBrush") : new SolidColorBrush(Color.FromRgb(59, 84, 109)), BorderThickness = new Thickness(1),
            Child = order is null
                ? new LauncherIcon { Kind = IconKind.Components, Width = 16, Height = 16, Foreground = (Brush)FindResource("GoldBrightBrush") }
                : new TextBlock { Text = order, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = active ? (Brush)FindResource("GoldBrightBrush") : (Brush)FindResource("TextMutedBrush") }
        };
        grid.Children.Add(badge);
        if (toggle is not null) { Grid.SetColumn(toggle, 1); grid.Children.Add(toggle); }
        var description = new StackPanel { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = GameSettingLabel(title); name.FontWeight = FontWeights.SemiBold;
        if (!active) name.Foreground = (Brush)FindResource("TextMutedBrush");
        description.Children.Add(name);
        description.Children.Add(new TextBlock { Text = details, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)FindResource("TextMutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        if (overrides is not null)
            description.Children.Add(new TextBlock { Text = overrides, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("GoldBrightBrush"), Margin = new Thickness(0, 4, 0, 0) });
        Grid.SetColumn(description, toggle is null ? 1 : 2);
        if (toggle is null) Grid.SetColumnSpan(description, 2);
        grid.Children.Add(description);
        if (actions is not null) { Grid.SetColumn(actions, 3); grid.Children.Add(actions); }
        return new Border { Child = grid, Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 8),
            CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromRgb(23, 47, 70)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(59, 84, 109)), BorderThickness = new Thickness(1) };
    }

    private void BuildCustomMods()
    {
        CustomModsBody.Children.Clear();
        CustomModsBody.Children.Add(CustomModRow(null, true, null, GameMod.Name(_customModsGame, _text.Language == "ru"),
            T("Базовая игра · всегда загружается первой", "Base game · always loads first"), null, null));
        var list = CurrentCustomMods;
        if (list.Count == 0)
        {
            var empty = GameSettingLabel(T("Пока нет пользовательских модов. Добавьте архив .rwd — исходный файл останется на месте.",
                "No custom mods yet. Add an .rwd archive; the original file will stay where it is."));
            empty.Margin = new Thickness(8, 16, 8, 16);
            CustomModsBody.Children.Add(empty);
            return;
        }
        var overrides = CustomRwdMods.Overrides(list.Select(m => m.Enabled ? (IReadOnlySet<string>?)CustomModResources(m) : null).ToList());
        int position = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var index = i; var mod = list[i];
            var check = new CheckBox { IsChecked = mod.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
            AutomationProperties.SetName(check, mod.Name);
            check.Checked += (_, _) => ChangeCustomMod(index, m => m with { Enabled = true });
            check.Unchecked += (_, _) => ChangeCustomMod(index, m => m with { Enabled = false });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            void Action(string label, string tip, Action action, bool enabled = true)
            {
                var button = new Button { Content = label, ToolTip = tip, Width = 30, Height = 30, Padding = new Thickness(0),
                    Margin = new Thickness(3, 0, 0, 0), Style = (Style)FindResource("GhostButton"), IsEnabled = enabled };
                AutomationProperties.SetName(button, tip + ": " + mod.Name);
                button.Click += (_, _) => action(); actions.Children.Add(button);
            }
            Action("↑", T("Выше", "Move up"), () => MoveCustomMod(index, -1), i > 0);
            Action("↓", T("Ниже", "Move down"), () => MoveCustomMod(index, 1), i + 1 < list.Count);
            Action("×", T("Убрать из списка", "Remove from list"), () => { CurrentCustomMods.RemoveAt(index); CustomModsChanged(); });
            var details = T("Файлов: ", "Files: ") + $"{mod.Files:N0} · " + FormatBytes(mod.Size) + " · SHA-256 " + mod.Sha256[..8].ToLowerInvariant();
            var replaced = overrides[i].Count == 0 ? null : T("Заменяет файлы: ", "Overrides files from: ")
                + string.Join(", ", overrides[i].Select(o => $"{list[o.Earlier].Name} ({o.Files:N0})"));
            CustomModsBody.Children.Add(CustomModRow(mod.Enabled ? (++position).ToString() : "–", mod.Enabled, check, mod.Name, details, replaced, actions));
        }
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
        BuildCustomMods(); RefreshCustomModsState(IsGameRunning());
    }

    private void RefreshCustomModsState(bool running)
    {
        if (CustomModsBody is null) return; // XAML initialization can refresh launch state before the page exists.
        // Never read IsEnabled back: it also reflects a disabled ancestor, such as the window body under a dialog.
        var editable = CanEditCustomMods && !running && !_customModsBusy;
        CustomModsBody.IsEnabled = editable;
        RefreshBaseGameButtons(running);
        CustomModsAdd.IsEnabled = editable && CurrentCustomMods.Count < CustomRwdMods.MaximumMods;
        var gated = _customModsState is not null && !CanEditCustomMods;
        ModManagerComponentsButton.Visibility = gated ? Visibility.Visible : Visibility.Collapsed;
        ModManagerStatusText.Text = _customModsError ?? (_customModsBusy ? T("Проверяю и применяю моды…", "Checking and applying mods…")
            : running ? T("Закройте игру, чтобы сохранить изменения.", "Close the game to save changes.")
            : gated ? T("Сначала установите или переключите выбранную игру выше.", "First install or switch to the selected game above.")
            : string.Join(" ", new[] { _customModsNotice, _customModsDirty ? T("Изменения ещё не применены.", "Changes are not applied yet.") : null }.Where(s => s is not null)));
        ModManagerStatusText.Visibility = ModManagerStatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        ModManagerApply.IsEnabled = CanEditCustomMods && _customModsDirty && !running && !_customModsBusy && !_busy && !_launchStarting;
        ModManagerRevert.IsEnabled = _customModsDirty && !_customModsBusy;
    }

    private async void CustomModsAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_customModsBusy || !CustomModsAdd.IsEnabled || _game is null) return;
        _customModsBusy = true; _customModsError = null; RefreshCustomModsState(IsGameRunning());
        try
        {
            var path = _chooseRwdMod is { } choose ? choose() : ChooseRwdMod();
            if (string.IsNullOrWhiteSpace(path)) return;
            var service = new CustomRwdMods(_game.Directory);
            var entry = await Task.Run(() => service.Import(path));
            if (!_customModsDraft.TryGetValue(_customModsGame, out var list)) _customModsDraft[_customModsGame] = list = [];
            var sameName = list.FindIndex(m => m.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            if (list.Any(m => m.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                _customModsError = T("Этот архив уже добавлен.", "This archive is already in the list.");
            else if (sameName >= 0)
            {
                // A rebuilt archive with the same file name replaces its entry in place: same position, same switch.
                list[sameName] = entry with { Enabled = list[sameName].Enabled };
                _customModsNotice = T("Заменён новым файлом: ", "Replaced with the new file: ") + entry.Name + ". "
                    + T("Нажмите «Отменить», чтобы вернуть прежний.", "Use Undo to keep the previous one.");
                CustomModsChanged(keepNotice: true);
            }
            else { list.Add(entry); CustomModsChanged(); }
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось добавить архив. Нужен исправный RWD для Kohan II размером до 2 ГБ.", "Could not add the archive. Choose a valid Kohan II RWD up to 2 GB."); }
        finally { _customModsBusy = false; RefreshCustomModsState(IsGameRunning()); }
    }

    private void ModManagerRevert_Click(object sender, RoutedEventArgs e) { if (!_customModsBusy) ReloadCustomMods(); }
    private async void ModManagerApply_Click(object sender, RoutedEventArgs e) => await SaveCustomModsAsync();

    private async Task SaveCustomModsAsync()
    {
        RefreshCustomModsState(IsGameRunning());
        if (!ModManagerApply.IsEnabled || _game is null || _customModsState?.AppliedSettings is null) return;
        var state = _customModsState; var game = _game.Directory; var draft = CustomRwdMods.Copy(_customModsDraft);
        _customModsBusy = true; _customModsError = _customModsNotice = null; RefreshCustomModsState(IsGameRunning());
        SetBusy(true, T("Проверяю и применяю моды…", "Checking and applying mods…"));
        try
        {
            EnsureGameClosed();
            await Task.Run(() => new ModuleInstaller(game).ReconcileAsync(state.Modules,
                settings: state.AppliedSettings, releaseId: state.ReleaseId, gameRequirement: state.GameRequirement,
                baseGameSha256: state.BaseGameSha256, customMods: draft,
                expectedState: JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), ensureGameClosed: EnsureGameClosed));
            _customModsDirty = false; _customModsState = new ModuleInstaller(game).LoadState();
            _customModsDraft = CustomRwdMods.Copy(_customModsState.CustomMods);
            InvalidateReadiness(); _fileCheckFailed = false;
            _customModsBusy = false;
            BuildCustomMods();
            RefreshStatus();
            ShowToast(() => T("Пользовательские моды применены.", "Custom mods applied."));
        }
        catch (Exception ex) { ActivityStore.Log(ex); _customModsError = T("Не удалось применить моды. Проверьте файлы и попробуйте ещё раз.", "Could not apply mods. Check the files and try again."); }
        finally { _customModsBusy = false; SetBusy(false); RefreshCustomModsState(IsGameRunning()); }
    }
}
