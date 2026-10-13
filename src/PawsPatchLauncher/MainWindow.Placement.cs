using System.ComponentModel;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private WindowPlacementPersistence? _windowPlacement;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_customModsBusy) { e.Cancel = true; return; }
        base.OnClosing(e); // All busy/modal/external cancellation handlers must run first.
        if (!e.Cancel) _windowPlacement?.SaveOnAcceptedClose();
    }
}
