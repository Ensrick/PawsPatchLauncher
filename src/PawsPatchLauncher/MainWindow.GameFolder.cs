using System.Diagnostics;
using System.Windows;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private bool _openingGameFolder;
    private Func<string, Task> _openGameFolder = path => Task.Run(() =>
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) throw new DirectoryNotFoundException(path);
        // Directory is a validated game installation; never build shell arguments from it.
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    });

    private void RefreshGameFolderButton()
    {
        OpenGameFolderButton.Content = T("Открыть папку игры", "Open game folder");
        OpenGameFolderButton.IsEnabled = _game is not null && !_openingGameFolder;
        OpenGameFolderButton.ToolTip = _game?.Directory ?? T("Сначала выберите папку Kohan II.", "Select the Kohan II folder first.");
        GamePathText.ToolTip = _game?.Directory;
        OpenSavesFolderButton.Content = T("Открыть папку сейвов", "Open saves folder");
        OpenSavesFolderButton.IsEnabled = !_openingSavesFolder;
        OpenSavesFolderButton.ToolTip = SavesDirectory;
    }

    private bool _openingSavesFolder;
    private Func<string> _savesDirectory = () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kohan2", "data", "Save");
    private string SavesDirectory => _savesDirectory();
    // The game's own data depot in Documents (autoexec.txt: adddepot %USERDATA%/data/ 1), included in file checks.
    private Func<string> _userDataDirectory = () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kohan2", "data");

    private async void OpenSavesFolderButton_Click(object sender, RoutedEventArgs e) => await OpenSavesFolderAsync();

    private async Task OpenSavesFolderAsync()
    {
        if (_openingSavesFolder || ConfirmationActive) return;
        _openingSavesFolder = true; RefreshGameFolderButton();
        try
        {
            var path = SavesDirectory;
            if (!Directory.Exists(path))
            {
                ShowToast(() => T("Папка сейвов ещё не создана. Сначала сохраните игру хотя бы один раз.",
                    "The saves folder does not exist yet. Save a game first."));
                return;
            }
            await _openGameFolder(path);
        }
        catch (Exception error)
        {
            ActivityStore.Log(error);
            ShowToast(() => T("Не удалось открыть папку сейвов.", "Could not open the saves folder."), true);
        }
        finally { _openingSavesFolder = false; RefreshGameFolderButton(); }
    }

    private async void OpenGameFolderButton_Click(object sender, RoutedEventArgs e) => await OpenGameFolderAsync();

    private async Task OpenGameFolderAsync()
    {
        if (_openingGameFolder || _game is null || ConfirmationActive) return;
        _openingGameFolder = true; RefreshGameFolderButton();
        try { await _openGameFolder(_game.Directory); }
        catch (Exception error)
        {
            ActivityStore.Log(error);
            ShowToast(() => T("Не удалось открыть папку игры. Проверьте, что она существует, или выберите её заново.",
                "Could not open the game folder. Check that it exists or select it again."), true);
        }
        finally { _openingGameFolder = false; RefreshGameFolderButton(); }
    }
}
