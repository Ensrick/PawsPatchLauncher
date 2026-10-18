using System.Diagnostics;
using System.Windows;

namespace PawsPatchLauncher;

// The single-player sandbox (GameSandbox): a switch in the Mod manager's list that makes Launch start the launcher's own
// copy of the game with the sandbox module. It is not an RWD: it has no load order and cannot be removed. Nothing is installed, so the switch needs no Apply and changes no configuration code.
public partial class MainWindow
{
    private string _sandboxExeSha256 = GameSandbox.SupportedK2ExeSha256;
    private Func<bool> _steamRunningProbe = DetectSteamRunning;

    private bool SandboxOffered => !GameMod.IsArcaneWars(_settings);

    /// <summary>The game folder holds the 1.3.68 executable and Launch starts that executable itself.</summary>
    private bool SandboxAvailable => SandboxOffered && _game is not null
        && string.Equals(_gameHash, _sandboxExeSha256, StringComparison.OrdinalIgnoreCase)
        && string.Equals(Path.GetFileName(ResolveLaunchExecutable(_game.Directory)), "k2.exe", StringComparison.OrdinalIgnoreCase);

    private bool SandboxSelected => _settings.SandboxEnabled && SandboxAvailable;

    private string LaunchButtonText => SandboxSelected ? T("Играть в песочнице", "Play sandbox") : _text["button.launch"];

    private void RefreshSandboxCard()
    {
        // The entry sits in the Mod manager panel, which only shows on its own page.
        SandboxCard.Visibility = SandboxOffered ? Visibility.Visible : Visibility.Collapsed;
        SandboxTitleText.Text = T("Песочница (одиночная игра)", "Sandbox (single player)");
        SandboxDescriptionText.Text = T(
            "Компьютерные игроки бездействуют. Щёлкните любой отряд или здание, чтобы управлять его королевством: панель ресурсов и меню строительства и найма переключаются на него. Общий обзор: вы видите то же, что видят все королевства игроков, как обзор союзника; дипломатия не меняется. Ресурсы другого королевства показаны вторым рядом под панелью ресурсов. Ctrl+Shift+A включает компьютерных игроков. Запуск идёт из отдельной копии игры: папка игры и сетевая игра не меняются.",
            "Computer players stay idle. Click any unit or building to control its kingdom: the resource bar and the build and recruit menus follow it. Shared vision: you see what every player kingdom sees, like an ally's sight; diplomacy is unchanged. The other kingdom's resources show as a second row under the resource bar. Ctrl+Shift+A turns computer players back on. Launch starts a separate copy of the game, so your game folder and multiplayer stay unchanged.");
        var available = SandboxAvailable;
        SandboxToggle.IsChecked = available && _settings.SandboxEnabled;
        SandboxToggle.IsEnabled = available && !_busy;
        SandboxCard.Opacity = available ? 1 : .42;
        SandboxCard.ToolTip = available ? null
            : T("Нужна Kohan II 1.3.68 (публичная ветка Steam) и запуск обычного k2.exe.", "Needs Kohan II 1.3.68 (the Steam public branch), started as the plain k2.exe.");
    }

    private void SandboxToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing || _busy) return;
        _settings.SandboxEnabled = SandboxToggle.IsChecked == true;
        _settingsStore.Save(_settings);
        ActionJournal.Record("sandbox", _settings.SandboxEnabled ? "enabled" : "disabled");
        RefreshSandboxCard();
        RefreshGameLaunchState();
    }

    /// <summary>Launch's last step: the game executable itself, or with the sandbox on, the brought-up-to-date copy.</summary>
    private async Task<Process?> StartGameProcessAsync(string executable)
    {
        if (!SandboxSelected || !string.Equals(Path.GetFileName(executable), "k2.exe", StringComparison.OrdinalIgnoreCase))
            return _startProcess(new ProcessStartInfo(executable) { WorkingDirectory = _game!.Directory, UseShellExecute = true });
        // The copy is outside the Steam library: it reaches the running Steam client through the app id in its environment.
        if (!_steamRunningProbe())
            throw new IOException(T("Сначала запустите Steam: песочница запускает свою копию игры.", "Start Steam first: the sandbox starts its own copy of the game."));
        var sandbox = new GameSandbox(_game!.Directory, GameSandbox.DefaultCopyRoot, _sandboxExeSha256);
        SetBusy(true, T("Обновляю копию игры для песочницы…", "Updating the sandbox copy of the game…"));
        var copied = await sandbox.SyncAsync(new Progress<double>(value => SetOperationProgress(value)));
        ActionJournal.Record("sandbox.copy", copied.ToString(System.Globalization.CultureInfo.InvariantCulture));
        EnsureGameClosed();
        return _startProcess(sandbox.StartInfo());
    }

    private void ShowLaunchResult(bool sandbox) => ShowResult(() => sandbox
        ? T("Песочница запущена: компьютерные игроки бездействуют.", "Sandbox launched: computer players stay idle.")
        : T("Игра запущена.", "Game launched."));

    private static bool DetectSteamRunning()
    {
        var processes = Process.GetProcessesByName("steam");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
