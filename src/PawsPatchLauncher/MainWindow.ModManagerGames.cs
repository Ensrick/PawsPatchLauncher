using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private static readonly string[] BaseGames = [GameMod.Vanilla, GameMod.Immortals];
    private readonly List<(string Mod, Button Button, bool Allowed)> _baseGameButtons = [];
    private bool _baseGameSwitching;

    private (string Channel, string? Pinned) BaseGameChannel(string mod)
    {
        if (mod == _settings.Mod) return (_settings.Channel, _settings.PinnedRelease);
        var choice = _settings.ModChannels?.GetValueOrDefault(mod);
        return (choice?.Channel == "beta" ? "beta" : "stable", choice?.PinnedRelease);
    }

    // 0 when every package is already on this computer, the bytes still to download otherwise,
    // null when no release list is known for the mod's channel.
    private long? BaseGameDownloadSize(string mod)
    {
        if (_game is null) return null;
        var (channel, pinned) = BaseGameChannel(mod);
        try
        {
            var installer = new ModuleInstaller(_game.Directory);
            var entry = new ModLibrary(_game.Directory).Find(mod, channel);
            if (entry is not null && (pinned is null || entry.ReleaseId == pinned)
                && ModLibrary.Packages(_feedClient.LoadArchived(entry.ReleaseId, entry.Channel), mod).All(p => LocallyAvailable(installer, p)))
                return 0;
            if (_feedClient.CachedGuideChannel(channel, pinned) is not { } offered) return mod == GameMod.Vanilla ? 0 : null;
            return ModLibrary.Packages(offered, mod).Where(p => !LocallyAvailable(installer, p)).Sum(p => p.Size);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or CryptographicException)
        { ActivityStore.Log(ex); return null; }
    }

    private void ApplyModManagerGamesLanguage()
    {
        ModManagerGamesTitleText.Text = T("Игра", "Game");
        ModManagerGamesDescriptionText.Text = T("Выберите игру. Недостающие файлы скачиваются и устанавливаются одним нажатием; моды ниже загружаются поверх неё.",
            "Choose the game. Missing files download and install in one click; the mods below load on top of it.");
    }

    private void BuildBaseGames()
    {
        ModManagerGamesBody.Children.Clear();
        _baseGameButtons.Clear();
        var applied = _customModsState?.AppliedSettings?.Mod;
        foreach (var mod in BaseGames)
        {
            var name = GameMod.Name(mod, _text.Language == "ru");
            if (mod == applied)
            {
                // On a game build without Paw's engine fixes (e.g. 1.3.68) the game runs with file changes only.
                var active = T("Установлена и активна", "Installed and active");
                if (_customModsState?.AppliedSettings?.DataOnly == true)
                    active += " · " + T("Только файловые изменения", "File changes only") + (_installedGameVersion.Length > 0 ? " · Kohan II " + _installedGameVersion : "");
                ModManagerGamesBody.Children.Add(CustomModRow(null, true, null, name, active, null, null));
                continue;
            }
            var locked = mod == GameMod.ArcaneWars;
            var size = locked ? null : BaseGameDownloadSize(mod);
            var details = locked ? ArcaneAccessReason
                : size is null ? T("Список выпусков ещё не загружен. Подключитесь к интернету и проверьте обновления.", "The release list is not loaded yet. Connect to the internet and check for updates.")
                : size == 0 ? T("Сохранена на этом компьютере · переключение без загрузки", "Stored on this computer · switches without a download")
                : T("Загрузка: ", "Download: ") + FormatBytes(size.Value);
            var button = new Button { Content = size == 0 ? T("Переключить", "Switch") : _text["button.install"], Padding = new Thickness(16, 7, 16, 7),
                Style = (Style)FindResource(size == 0 ? "GhostButton" : "GoldButton") };
            if (locked) button.ToolTip = ArcaneAccessReason;
            AutomationProperties.SetName(button, button.Content + ": " + name);
            button.Click += async (_, _) => await SwitchBaseGameAsync(mod);
            var allowed = !locked && size is not null;
            _baseGameButtons.Add((mod, button, allowed));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(button);
            ModManagerGamesBody.Children.Add(CustomModRow(null, false, null, name, details, null, actions));
        }
        RefreshBaseGameButtons(IsGameRunning());
    }

    private void RefreshBaseGameButtons(bool running)
    {
        var idle = _game is not null && !running && !_busy && !_launchStarting && !_customModsBusy && !_baseGameSwitching && !ConfirmationActive;
        foreach (var (_, button, allowed) in _baseGameButtons) button.IsEnabled = idle && allowed;
    }

    // One click: the same selection as the Components page, then Paw's install (download, verify, apply)
    // when files are missing, or a plain apply when the mod is already stored.
    private async Task SwitchBaseGameAsync(string mod)
    {
        if (_game is null || _busy || _customModsBusy || _baseGameSwitching || ConfirmationActive) return;
        if (_customModsDirty)
        {
            _customModsError = T("Сначала примените или отмените изменения модов.", "Apply or undo your mod changes first.");
            RefreshCustomModsState(IsGameRunning());
            return;
        }
        _baseGameSwitching = true; _customModsError = null;
        RefreshCustomModsState(IsGameRunning());
        try
        {
            if (!await SelectModAsync(mod)) return;
            RefreshStatus();
            if (!_patchInstalled || _selectionRequiresUpdate) await InstallSelectedModAsync();
            else await ApplySettingsAsync();
        }
        finally
        {
            _baseGameSwitching = false;
            if (_activePage == "modmanager") ReloadCustomMods();
        }
    }
}
