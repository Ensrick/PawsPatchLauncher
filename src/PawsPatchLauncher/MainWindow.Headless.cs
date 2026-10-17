using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace PawsPatchLauncher;

// `--headless <command>` runs this window off-screen. Every command drives the same methods and controls as the
// window's own buttons and switches, so availability rules, transactions and messages are the launcher's own.
public partial class MainWindow
{
    private bool _headless = HeadlessMode.Active;
    private TextWriter? _headlessOutput;
    private bool _headlessFailed;
    private int? _headlessLaunched;
    private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Func<ProcessStartInfo, Process?> _startProcess = Process.Start;
    private static readonly TimeSpan HeadlessStepTimeout = TimeSpan.FromMinutes(3), HeadlessInstallTimeout = TimeSpan.FromMinutes(30);

    private sealed class HeadlessUsageException(string message) : Exception(message);
    private sealed class HeadlessBusyException(string message) : Exception(message);
    private sealed record HeadlessSwitch(string Key, ToggleButton Control, FrameworkElement Card, Func<bool> Read);

    private void HeadlessReport(string text, bool failure)
    {
        if (failure) _headlessFailed = true;
        _headlessOutput?.WriteLine((failure ? "error: " : "") + text);
    }

    internal async Task<int> RunHeadlessCommandAsync(IReadOnlyList<string> args, TextWriter output)
    {
        _headless = true; _headlessOutput = output; _headlessFailed = false; _headlessLaunched = null;
        try
        {
            if (args.Count == 0 || HeadlessMode.IsHelp(args[0]))
            {
                WriteHeadlessHelp(output, _text.Language);
                return args.Count == 0 ? HeadlessMode.Usage : HeadlessMode.Success;
            }
            var command = args[0].ToLowerInvariant();
            var rest = args.Skip(1).ToList();
            if (command is not ("status" or "select-game" or "set" or "mods" or "apply" or "launch"))
                throw new HeadlessUsageException(TF($"Неизвестная команда: {command}. Список команд: --headless help",
                    $"Unknown command: {command}. For the list of commands: --headless help"));
            await _initialized.Task.WaitAsync(HeadlessStepTimeout);
            RefreshStatus();
            await WaitForHeadlessIdleAsync(HeadlessStepTimeout);
            return command switch
            {
                "status" => HeadlessStatus(output, rest),
                "select-game" => await HeadlessSelectGameAsync(output, rest),
                "set" => await HeadlessSetAsync(output, rest),
                "mods" => await HeadlessModsAsync(output, rest),
                "apply" => await HeadlessApplyAsync(output, rest),
                _ => await HeadlessLaunchAsync(output, rest)
            };
        }
        catch (HeadlessUsageException error) { output.WriteLine("error: " + error.Message); return HeadlessMode.Usage; }
        catch (HeadlessBusyException error) { output.WriteLine("error: " + error.Message); return HeadlessMode.Busy; }
        catch (Exception error) { ActivityStore.Log(error); output.WriteLine("error: " + error.Message); return HeadlessMode.Failure; }
        finally { output.Flush(); _headlessOutput = null; }
    }

    private async Task WaitForHeadlessIdleAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        // A background update check never holds a command longer than a minute; offline, cached releases are used.
        var feedDeadline = DateTime.UtcNow + TimeSpan.FromMinutes(1);
        while (true)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var feed = _checkingFeed && (!_backgroundFeedCheck || DateTime.UtcNow < feedDeadline);
            if (!_busy && !feed && !_launchStarting && !_customModsBusy && _compatibilityState != GameCompatibilityState.Checking) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(T("Лаунчер не завершил операцию вовремя.", "The launcher did not finish the operation in time."));
            await Task.Delay(50);
        }
    }

    private void EnsureHeadlessGameClosed()
    {
        if (IsGameRunning()) throw new HeadlessBusyException(T("Kohan II запущен. Закройте игру и повторите команду.", "Kohan II is running. Close the game and run the command again."));
    }

    private static HeadlessUsageException HeadlessUsage(string usage) => new("usage: --headless " + usage);
    private static string OnOff(bool value) => value ? "on" : "off";
    private static string HeadlessChannel(string channel) => channel == "beta" ? "beta" : "release";

    private InstallState? HeadlessState()
    {
        try { return _game is null ? null : new ModuleInstaller(_game.Directory).LoadState(); }
        catch (Exception error) { ActivityStore.Log(error); return null; }
    }

    // ---- status ----

    private int HeadlessStatus(TextWriter o, List<string> args)
    {
        if (args.Count > 0) throw HeadlessUsage("status");
        RefreshStatus();
        var state = HeadlessState();
        o.WriteLine("game: " + (_game?.Directory ?? T("не найдена", "not found")));
        if (_game is not null)
        {
            o.WriteLine("game-version: " + (_installedGameVersion.Length > 0 ? _installedGameVersion : T("не определена", "unknown"))
                + $" (Steam {(_game.Branch == "beta" ? "beta" : "public")}, build {_game.SteamBuild ?? "?"})");
            o.WriteLine("game-support: " + _compatibilityState switch
            {
                GameCompatibilityState.Supported => T("поддерживается, все функции патча", "supported, all patch features"),
                GameCompatibilityState.Unsupported => T("только файловые изменения", "file changes only"),
                GameCompatibilityState.Checking => T("проверяется", "checking"),
                GameCompatibilityState.Unavailable => T("не удалось проверить файлы игры", "could not verify the game files"),
                _ => T("не проверялась", "not checked")
            } + (_installedRuntimeMismatch ? T("; установленному патчу нужно обновление", "; the installed patch needs an update") : ""));
        }
        o.WriteLine("selected-game: " + _settings.Mod);
        o.WriteLine("channel: " + HeadlessChannel(_settings.Channel));
        var offered = HeadlessOfferedCards();
        var components = HeadlessComponentValues();
        o.WriteLine("components: " + string.Join(" ", components.Select(c => c.Key + "=" + c.Value)));
        var hidden = components.Where(c => !offered.Contains(c.Card)).Select(c => c.Key).ToArray();
        if (hidden.Length > 0) o.WriteLine("not-offered: " + string.Join(", ", hidden));
        o.WriteLine($"language: text={GameLanguages.Text(_settings)} voice={GameLanguages.Voice(_settings)}");
        HeadlessWriteMods(o, _settings.Mod, state?.CustomMods.GetValueOrDefault(_settings.Mod) ?? []);
        HeadlessWriteApplied(o, state);
        o.WriteLine("installed: " + (_patchInstalled ? "yes" : "no") + "; update-available: " + (_patchUpdateAvailable || _selectionRequiresUpdate ? "yes" : "no")
            + "; pending: " + (_settingsPending ? "yes" : "no"));
        o.WriteLine("status: " + ReadyStatusText.Text);
        o.WriteLine("launch: " + (LaunchButton.IsEnabled ? "ready" : "blocked"));
        if (_feedFailure is not null) o.WriteLine("updates: " + _feedFailure());
        if (_incident is not null) o.WriteLine("notice: " + _incident);
        return HeadlessMode.Success;
    }

    private (string Key, string Value, FrameworkElement Card)[] HeadlessComponentValues()
    {
        var arcane = GameMod.IsArcaneWars(_settings);
        var pure = GameMod.PureComponents(_settings);
        return [
            ("paw", OnOff(GameMod.PawPatchSelected(_settings)), CoreModuleCard),
            ("colors", OnOff(GameMod.ColorsSelected(_settings)), ColorsModuleCard),
            ("ignore-desync", OnOff(GameMod.DesyncSelected(_settings)), OosModuleCard),
            ("hostility", OnOff(arcane ? _settings.IndependentHostility : pure.IndependentHostility), IndependentHostilityCard),
            ("roaming", OnOff(arcane ? _settings.AdditionalRoamingCompanies : pure.AdditionalRoamingCompanies), AdditionalRoamingCard),
            ("spawn", arcane ? _settings.RoamingSpawnMode : pure.RoamingSpawnMode, RoamingSpawnCard),
            ("ai", OnOff(GameMod.ImprovedAiSelected(_settings)), ImprovedAiCard),
            ("lair", OnOff(pure.WoundedLairDefenders), PureLairCard)];
    }

    // The Components page decides which cards it shows; reuse that layout pass instead of restating its rules.
    private HashSet<FrameworkElement> HeadlessOfferedCards()
    {
        var page = _activePage;
        _activePage = "modules";
        try
        {
            RefreshModControls();
            FrameworkElement[] cards = [CoreModuleCard, ColorsModuleCard, OosModuleCard, IndependentHostilityCard, AdditionalRoamingCard,
                RoamingSpawnCard, ImprovedAiCard, PureLairCard, PatchChannelCard, RussianModuleCard];
            return cards.Where(card => card.Visibility == Visibility.Visible).ToHashSet();
        }
        finally { _activePage = page; RefreshModControls(); }
    }

    private void HeadlessWriteApplied(TextWriter o, InstallState? state)
    {
        if (state?.AppliedSettings is not { } applied) { o.WriteLine("applied: " + T("ничего", "nothing")); return; }
        o.WriteLine($"applied: {applied.Mod} {HeadlessChannel(applied.Channel)} paw={OnOff(GameMod.PawPatchSelected(applied))}"
            + (applied.DataOnly ? " " + T("только файловые изменения", "file changes only") : ""));
        o.WriteLine("modules: " + (state.Modules.Count == 0 ? T("нет", "none")
            : string.Join(", ", state.Modules.OrderBy(m => m.Key, StringComparer.OrdinalIgnoreCase).Select(m => m.Key + " " + m.Value.Version))));
    }

    private void HeadlessWriteMods(TextWriter o, string game, IReadOnlyList<CustomRwdMod> mods)
    {
        o.WriteLine($"mods ({game}): " + (mods.Count == 0 ? T("нет", "none") : mods.Count.ToString(CultureInfo.InvariantCulture)));
        for (var i = 0; i < mods.Count; i++)
            o.WriteLine($"  {i + 1}. [{OnOff(mods[i].Enabled)}] {mods[i].Name} ({mods[i].Files:N0} " + T("файлов", "files") + ", " + FormatBytes(mods[i].Size) + ")");
    }

    // ---- select-game ----

    private async Task<int> HeadlessSelectGameAsync(TextWriter o, List<string> args)
    {
        if (args.Count != 1) throw HeadlessUsage("select-game vanilla|immortals");
        var mod = args[0].ToLowerInvariant() switch
        {
            "vanilla" => GameMod.Vanilla,
            "immortals" => GameMod.Immortals,
            _ => throw HeadlessUsage("select-game vanilla|immortals")
        };
        EnsureHeadlessGameClosed();
        if (_settings.Mod == mod) o.WriteLine("selected-game: " + mod + " (" + T("без изменений", "unchanged") + ")");
        else
        {
            var radio = mod == GameMod.Immortals ? ImmortalsModRadio : VanillaModRadio;
            if (!radio.IsEnabled) { HeadlessReport(T("Выбор игры сейчас недоступен: ", "The game cannot be changed right now: ") + ReadyStatusText.Text, true); return HeadlessMode.Failure; }
            radio.IsChecked = true;
            await WaitForHeadlessIdleAsync(HeadlessStepTimeout);
            if (_settings.Mod != mod) { HeadlessReport(T("Лаунчер не сменил игру.", "The launcher kept the previous game."), true); return HeadlessMode.Failure; }
            o.WriteLine("selected-game: " + mod);
        }
        RefreshStatus();
        o.WriteLine("status: " + ReadyStatusText.Text);
        if (!_patchInstalled || _settingsPending || _selectionRequiresUpdate)
            o.WriteLine(T("Чтобы установить или применить: --headless apply", "To install or apply: --headless apply"));
        return HeadlessMode.Success;
    }

    // ---- set ----

    private static readonly string[] HeadlessSwitchKeys = ["paw", "colors", "ignore-desync", "hostility", "roaming", "ai", "lair"];

    private async Task<int> HeadlessSetAsync(TextWriter o, List<string> args)
    {
        const string usage = "set key=value [key=value ...]   (--headless help lists the keys)";
        if (args.Count == 0) throw HeadlessUsage(usage);
        var changes = new List<(string Key, string Value)>();
        foreach (var pair in args)
        {
            var at = pair.IndexOf('=');
            if (at <= 0 || at == pair.Length - 1) throw HeadlessUsage(usage);
            var key = pair[..at].Trim().ToLowerInvariant(); var value = pair[(at + 1)..].Trim().ToLowerInvariant();
            var valid = key switch
            {
                "channel" => value is "release" or "stable" or "beta",
                "spawn" => value is "standard" or "x2" or "x4",
                "text" => GameLanguages.Choices.Contains(value, StringComparer.OrdinalIgnoreCase),
                "voice" => GameLanguages.VoiceChoices.Contains(value, StringComparer.OrdinalIgnoreCase),
                _ when HeadlessSwitchKeys.Contains(key) => HeadlessSwitchValue(value) is not null,
                _ => throw new HeadlessUsageException(TF($"Неизвестный ключ: {key}. Ключи: --headless help", $"Unknown key: {key}. For the keys: --headless help"))
            };
            if (!valid) throw new HeadlessUsageException(TF($"Недопустимое значение: {pair}", $"Invalid value: {pair}"));
            changes.Add((key, value));
        }
        EnsureHeadlessGameClosed();
        foreach (var (key, value) in changes)
        {
            var done = key switch
            {
                "channel" => await HeadlessChannelAsync(o, value is "beta" ? "beta" : "stable"),
                "spawn" => await HeadlessSpawnAsync(o, value),
                "text" or "voice" => await HeadlessLanguageAsync(o, key, value),
                _ => await HeadlessToggleAsync(o, key, HeadlessSwitchValue(value)!.Value)
            };
            if (!done) return HeadlessMode.Failure;
        }
        RefreshStatus();
        o.WriteLine("pending: " + (_settingsPending ? T("да, выполните --headless apply", "yes, run --headless apply") : T("нет", "no")));
        return HeadlessMode.Success;
    }

    private static bool? HeadlessSwitchValue(string value) => value switch
    {
        "on" or "true" or "yes" or "1" => true,
        "off" or "false" or "no" or "0" => false,
        _ => null
    };

    private HeadlessSwitch HeadlessSwitchFor(string key)
    {
        var arcane = GameMod.IsArcaneWars(_settings);
        return key switch
        {
            "paw" => new(key, PawPatchToggle, CoreModuleCard, () => GameMod.PawPatchSelected(_settings)),
            "colors" => new(key, ColorsToggle, ColorsModuleCard, () => GameMod.ColorsSelected(_settings)),
            "ignore-desync" => new(key, IgnoreDesyncToggle, OosModuleCard, () => GameMod.DesyncSelected(_settings)),
            "hostility" => new(key, IndependentHostilityToggle, IndependentHostilityCard,
                () => arcane ? _settings.IndependentHostility : GameMod.PureComponents(_settings).IndependentHostility),
            "roaming" => new(key, AdditionalRoamingToggle, AdditionalRoamingCard,
                () => arcane ? _settings.AdditionalRoamingCompanies : GameMod.PureComponents(_settings).AdditionalRoamingCompanies),
            "ai" => new(key, ImprovedAiToggle, ImprovedAiCard, () => GameMod.ImprovedAiSelected(_settings)),
            _ => new(key, PureLairToggle, PureLairCard, () => GameMod.PureComponents(_settings).WoundedLairDefenders)
        };
    }

    private bool HeadlessUnavailable(string key, FrameworkElement card, Control control)
    {
        if (!HeadlessOfferedCards().Contains(card))
        {
            HeadlessReport(TF($"{key}: не предлагается для {GameMod.Name(_settings.Mod, true)} на этом канале и версии игры.",
                $"{key}: not offered for {GameMod.Name(_settings.Mod)} on this channel and game version."), true);
            return true;
        }
        if (control.IsEnabled) return false;
        var reason = control.ToolTip as string ?? card.ToolTip as string ?? T("переключатель сейчас недоступен.", "the switch is unavailable right now.");
        HeadlessReport(key + ": " + reason, true);
        return true;
    }

    private async Task<bool> HeadlessToggleAsync(TextWriter o, string key, bool value)
    {
        var item = HeadlessSwitchFor(key);
        if (item.Read() == value) { o.WriteLine($"{key}: {OnOff(value)} (" + T("без изменений", "unchanged") + ")"); return true; }
        if (HeadlessUnavailable(key, item.Card, item.Control)) return false;
        item.Control.IsChecked = value;
        item.Control.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await WaitForHeadlessIdleAsync(HeadlessStepTimeout);
        if (item.Read() != value)
        {
            HeadlessReport(TF($"{key}: лаунчер оставил {OnOff(!value)}.", $"{key}: the launcher kept it {OnOff(!value)}."), true);
            return false;
        }
        o.WriteLine($"{key}: {OnOff(value)}");
        return true;
    }

    private async Task<bool> HeadlessSelectAsync(TextWriter o, string key, string value, FrameworkElement card, RadioButton radio, Func<string> read)
    {
        if (read() == value) { o.WriteLine($"{key}: {value} (" + T("без изменений", "unchanged") + ")"); return true; }
        if (HeadlessUnavailable(key, card, radio)) return false;
        radio.IsChecked = false; radio.IsChecked = true;
        await WaitForHeadlessIdleAsync(HeadlessStepTimeout);
        if (read() != value)
        {
            HeadlessReport(TF($"{key}: лаунчер оставил {read()}.", $"{key}: the launcher kept {read()}."), true);
            return false;
        }
        o.WriteLine($"{key}: {value}");
        return true;
    }

    private Task<bool> HeadlessChannelAsync(TextWriter o, string channel)
        => HeadlessSelectAsync(o, "channel", HeadlessChannel(channel), PatchChannelCard, channel == "beta" ? ModBetaRadio : ModReleaseRadio,
            () => HeadlessChannel(_settings.Channel));

    private Task<bool> HeadlessSpawnAsync(TextWriter o, string mode)
        => HeadlessSelectAsync(o, "spawn", mode, RoamingSpawnCard, mode == "x2" ? X2SpawnRadio : mode == "x4" ? X4SpawnRadio : StandardSpawnRadio,
            () => GameMod.IsArcaneWars(_settings) ? _settings.RoamingSpawnMode : GameMod.PureComponents(_settings).RoamingSpawnMode);

    private async Task<bool> HeadlessLanguageAsync(TextWriter o, string key, string code)
    {
        var combo = key == "text" ? GameLanguageCombo : GameVoiceCombo;
        string Read() => key == "text" ? GameLanguages.Text(_settings) : GameLanguages.Voice(_settings);
        if (string.Equals(Read(), code, StringComparison.OrdinalIgnoreCase)) { o.WriteLine($"{key}: {Read()} (" + T("без изменений", "unchanged") + ")"); return true; }
        if (HeadlessUnavailable(key, RussianModuleCard, combo)) return false;
        var choice = (combo.ItemsSource as IEnumerable<LanguageChoice>)?.FirstOrDefault(c => c.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (choice is null) { HeadlessReport(TF($"{key}: язык {code} недоступен.", $"{key}: language {code} is not available."), true); return false; }
        combo.SelectedItem = choice;
        await WaitForHeadlessIdleAsync(HeadlessStepTimeout);
        if (!string.Equals(Read(), code, StringComparison.OrdinalIgnoreCase))
        {
            HeadlessReport(TF($"{key}: лаунчер оставил {Read()}.", $"{key}: the launcher kept {Read()}."), true);
            return false;
        }
        o.WriteLine($"{key}: {Read()}");
        return true;
    }

    // ---- mods ----

    private async Task<int> HeadlessModsAsync(TextWriter o, List<string> args)
    {
        const string usage = "mods [list] | mods add <file.rwd> [--enable] | mods remove|enable|disable <name|number> | mods move <name|number> <position>   [--game vanilla|immortals]";
        var game = _settings.Mod;
        var gameAt = args.FindIndex(a => a.Equals("--game", StringComparison.OrdinalIgnoreCase));
        if (gameAt >= 0)
        {
            if (gameAt + 1 >= args.Count) throw HeadlessUsage(usage);
            game = args[gameAt + 1].ToLowerInvariant() switch
            {
                "vanilla" => GameMod.Vanilla,
                "immortals" => GameMod.Immortals,
                _ => throw HeadlessUsage(usage)
            };
            args.RemoveRange(gameAt, 2);
        }
        var enable = args.RemoveAll(a => a.Equals("--enable", StringComparison.OrdinalIgnoreCase)) > 0;
        var action = args.Count == 0 ? "list" : args[0].ToLowerInvariant();
        if (_game is null) { HeadlessReport(_text["status.notfound"], true); return HeadlessMode.Failure; }
        var state = new ModuleInstaller(_game.Directory).LoadState();
        if (action == "list")
        {
            if (args.Count > 1 || enable) throw HeadlessUsage(usage);
            HeadlessWriteMods(o, game, state.CustomMods.GetValueOrDefault(game) ?? []);
            o.WriteLine(T("Нижний включённый мод имеет приоритет при совпадении файлов.", "Enabled mods lower in the list take priority when files overlap."));
            return HeadlessMode.Success;
        }
        var arguments = action switch { "add" or "remove" or "enable" or "disable" => 2, "move" => 3, _ => throw HeadlessUsage(usage) };
        if (args.Count != arguments || enable && action != "add") throw HeadlessUsage(usage);
        EnsureHeadlessGameClosed();
        if (state.AppliedSettings is null)
        {
            HeadlessReport(T("Сначала установите и примените игру: --headless apply", "Install and apply a game first: --headless apply"), true);
            return HeadlessMode.Failure;
        }
        var draft = CustomRwdMods.Copy(state.CustomMods);
        if (!draft.TryGetValue(game, out var list)) draft[game] = list = [];
        string result;
        if (action == "add")
        {
            var path = Path.GetFullPath(args[1]);
            if (!File.Exists(path)) { HeadlessReport(TF($"Файл не найден: {path}", $"File not found: {path}"), true); return HeadlessMode.Failure; }
            var directory = _game.Directory;
            CustomRwdMod entry;
            try { entry = await Task.Run(() => new CustomRwdMods(directory).Import(path)); }
            catch (Exception error)
            {
                ActivityStore.Log(error);
                HeadlessReport(T("Не удалось добавить архив. Нужен исправный RWD для Kohan II размером до 2 ГБ.", "Could not add the archive. Choose a valid Kohan II RWD up to 2 GB.")
                    + " (" + error.Message + ")", true);
                return HeadlessMode.Failure;
            }
            var existing = list.FindIndex(m => m.Sha256.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase));
            if (existing >= 0 && !(enable && !list[existing].Enabled))
            {
                o.WriteLine(T("Этот архив уже добавлен.", "This archive is already in the list."));
                HeadlessWriteMods(o, game, list);
                return HeadlessMode.Success;
            }
            var sameName = existing >= 0 ? existing : list.FindIndex(m => m.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase));
            if (sameName >= 0)
            {
                // A rebuilt archive with the same file name replaces its entry in place: same position, same switch.
                list[sameName] = entry with { Enabled = enable || list[sameName].Enabled };
                result = (existing >= 0 ? T("Включён: ", "Enabled: ") : T("Заменён новым файлом: ", "Replaced with the new file: ")) + entry.Name;
            }
            else
            {
                if (list.Count >= CustomRwdMods.MaximumMods)
                {
                    HeadlessReport(TF($"В списке уже {CustomRwdMods.MaximumMods} модов.", $"The list already has {CustomRwdMods.MaximumMods} mods."), true);
                    return HeadlessMode.Failure;
                }
                list.Add(entry with { Enabled = enable });
                result = T("Добавлен: ", "Added: ") + entry.Name;
            }
        }
        else
        {
            var index = HeadlessFindMod(list, args[1]);
            if (index < 0) { HeadlessReport(TF($"Мод не найден: {args[1]}", $"Mod not found: {args[1]}"), true); return HeadlessMode.Failure; }
            var name = list[index].Name;
            switch (action)
            {
                case "remove":
                    list.RemoveAt(index);
                    result = T("Убран из списка: ", "Removed from the list: ") + name;
                    break;
                case "enable" or "disable":
                    var on = action == "enable";
                    if (list[index].Enabled == on) { o.WriteLine($"{name}: {OnOff(on)} (" + T("без изменений", "unchanged") + ")"); return HeadlessMode.Success; }
                    list[index] = list[index] with { Enabled = on };
                    result = name + ": " + OnOff(on);
                    break;
                default:
                    if (!int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out var position) || position < 1 || position > list.Count)
                        throw new HeadlessUsageException(TF($"Позиция должна быть от 1 до {list.Count}.", $"The position must be from 1 to {list.Count}."));
                    var mod = list[index]; list.RemoveAt(index); list.Insert(position - 1, mod);
                    result = TF($"{name}: позиция {position}", $"{name}: position {position}");
                    break;
            }
        }
        if (!await HeadlessSaveCustomModsAsync(state, draft)) return HeadlessMode.Failure;
        o.WriteLine(result);
        HeadlessWriteMods(o, game, list);
        o.WriteLine(state.AppliedSettings.Mod == game
            ? T("Включённые моды подключены к игре.", "Enabled mods are mounted in the game.")
            : TF($"Сохранено для {GameMod.Name(game, true)}; моды подключатся, когда эта игра будет применена.",
                $"Saved for {GameMod.Name(game)}; the mods are mounted when that game is applied."));
        return HeadlessMode.Success;
    }

    private static int HeadlessFindMod(List<CustomRwdMod> list, string name)
    {
        var exact = list.FindIndex(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact >= 0) return exact;
        return int.TryParse(name.TrimStart('#'), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= list.Count ? number - 1 : -1;
    }

    // The same installer transaction as the window's "Apply mods": the applied game and its packages stay as they are.
    private async Task<bool> HeadlessSaveCustomModsAsync(InstallState state, Dictionary<string, List<CustomRwdMod>> draft)
    {
        if (_game is null) return false;
        var game = _game.Directory;
        var mods = CustomRwdMods.Copy(draft);
        SetBusy(true, T("Проверяю и применяю моды…", "Checking and applying mods…"));
        try
        {
            EnsureGameClosed();
            await Task.Run(() => new ModuleInstaller(game).ReconcileAsync(state.Modules,
                settings: state.AppliedSettings, releaseId: state.ReleaseId, gameRequirement: state.GameRequirement,
                baseGameSha256: state.BaseGameSha256, customMods: mods,
                expectedState: JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), ensureGameClosed: EnsureGameClosed));
            InvalidateReadiness(); _fileCheckFailed = false;
            return true;
        }
        catch (Exception error) { ShowError(error); return false; }
        finally { SetBusy(false); RefreshStatus(); }
    }

    // ---- apply ----

    private async Task<int> HeadlessApplyAsync(TextWriter o, List<string> args)
    {
        if (args.Count > 0) throw HeadlessUsage("apply");
        if (_game is null) { HeadlessReport(_text["status.notfound"], true); return HeadlessMode.Failure; }
        EnsureHeadlessGameClosed();
        var steps = 0;
        // The window's primary action: Install/Update when the selection is not stored, then Apply settings.
        for (var step = 0; step < 2 && !_headlessFailed; step++)
        {
            RefreshStatus();
            if (!_patchInstalled || _patchUpdateAvailable || _selectionRequiresUpdate)
            {
                if (!UpdateButton.IsEnabled)
                {
                    HeadlessReport(T("Установка или обновление сейчас недоступны: ", "Install or update is not available: ") + ReadyStatusText.Text, true);
                    break;
                }
                o.WriteLine("action: " + UpdateButton.Content);
                UpdateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            }
            else if (_settingsPending)
            {
                o.WriteLine("action: " + ApplySettingsButton.Content);
                await ApplySettingsAsync();
            }
            else break;
            steps++;
            await WaitForHeadlessIdleAsync(HeadlessInstallTimeout);
        }
        RefreshStatus();
        if (!_headlessFailed && (_settingsPending || !_patchInstalled || _selectionRequiresUpdate))
            HeadlessReport(T("Настройки не применены: ", "The selection is not applied: ") + ReadyStatusText.Text, true);
        if (steps == 0 && !_headlessFailed) o.WriteLine(T("Применять нечего.", "Nothing to apply."));
        HeadlessWriteApplied(o, HeadlessState());
        o.WriteLine("status: " + ReadyStatusText.Text);
        return _headlessFailed ? HeadlessMode.Failure : HeadlessMode.Success;
    }

    // ---- launch ----

    private async Task<int> HeadlessLaunchAsync(TextWriter o, List<string> args)
    {
        if (args.Count > 0) throw HeadlessUsage("launch");
        if (_game is null) { HeadlessReport(_text["status.notfound"], true); return HeadlessMode.Failure; }
        EnsureHeadlessGameClosed();
        RefreshStatus();
        if (!LaunchButton.IsEnabled)
        {
            HeadlessReport(T("Запуск недоступен: ", "Launch is not available: ") + ReadyStatusText.Text, true);
            return HeadlessMode.Failure;
        }
        await LaunchGameAsync();
        await WaitForHeadlessIdleAsync(HeadlessStepTimeout);
        if (_headlessLaunched is { } process)
        {
            o.WriteLine("launched: process " + process.ToString(CultureInfo.InvariantCulture));
            return HeadlessMode.Success;
        }
        if (!_headlessFailed) HeadlessReport(T("Игра не запущена: ", "The game was not started: ") + ReadyStatusText.Text, true);
        return HeadlessMode.Failure;
    }

    // ---- help ----

    internal static void WriteHeadlessHelp(TextWriter o, string language)
    {
        string T(string ru, string en) => UiLanguages.Text(language, ru, en);
        var exe = Path.GetFileName(Environment.ProcessPath) is { Length: > 0 } name && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            && !name.StartsWith("PreviewRenderer", StringComparison.OrdinalIgnoreCase) ? name : "PawsPatchLauncher.exe";
        o.WriteLine(exe + " --headless <command> [--test-profile=<folder>]");
        o.WriteLine(T("Лаунчер работает без окна: те же проверки, установка и сообщения, что и у кнопок окна.",
            "Runs the launcher without its window: the same checks, installation and messages as the window's buttons."));
        o.WriteLine();
        o.WriteLine(T("Команды:", "Commands:"));
        o.WriteLine("  status                          " + T("игра, выбор, компоненты, моды, применённое состояние, готовность", "game, selection, components, mods, applied state, ready or blocked"));
        o.WriteLine("  select-game vanilla|immortals   " + T("выбрать игру для установки и запуска", "choose the game to install and start"));
        o.WriteLine("  set key=value [key=value ...]   " + T("изменить выбор, как на странице «Компоненты»", "change the selection, like the Components page"));
        o.WriteLine("      channel=release|beta  paw=on|off  colors=on|off  ignore-desync=on|off  hostility=on|off");
        o.WriteLine("      roaming=on|off  spawn=standard|x2|x4  ai=on|off  lair=on|off");
        o.WriteLine("      text=" + string.Join("|", GameLanguages.Choices) + "  voice=" + string.Join("|", GameLanguages.VoiceChoices));
        o.WriteLine("      " + T("Меняются только компоненты, которые страница предлагает для этой игры, канала и версии игры.",
            "Only components the page offers for this game, channel and game version can change."));
        o.WriteLine("  mods [--game vanilla|immortals] " + T("список RWD-модов (нижний включённый имеет приоритет)", "list RWD mods (enabled mods lower in the list win)"));
        o.WriteLine("  mods add <file.rwd> [--enable] [--game <game>]   " + T("добавить; файл с тем же именем заменяет прежний", "add; a file with the same name replaces the old one"));
        o.WriteLine("  mods remove|enable|disable <name|number> [--game <game>]");
        o.WriteLine("  mods move <name|number> <position> [--game <game>]");
        o.WriteLine("  apply                           " + T("установить, обновить или применить настройки, как главная кнопка", "install, update or apply settings, like the main button"));
        o.WriteLine("  launch                          " + T("запустить игру, как кнопка «Играть»", "start the game, like Launch"));
        o.WriteLine("  help");
        o.WriteLine();
        o.WriteLine(T("Коды выхода: 0 готово, 1 ошибка, 2 неверная команда, 3 открыто окно лаунчера или запущена игра.",
            "Exit codes: 0 done, 1 failed, 2 usage error, 3 a launcher window is open or the game is running."));
        o.WriteLine(T("В PowerShell дождитесь результата так: ", "In PowerShell, wait for the result with: ") + $"& .\\{exe} --headless status | Out-Host");
    }
}
