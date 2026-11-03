namespace PawsPatchLauncher;

// Paw's launcher 1.0.0 made Arcane Wars public (base mod from ModDB, Darquan's patch applied separately), so this
// launcher without accounts offers it like the other games. The names stay so Paw's call sites merge unchanged.
public partial class MainWindow
{
    private bool ArcaneAccessBlocked => false;
    private string ArcaneAccessReason => string.Empty;

    private void EnsureModAccess(string mod) { }

    private void EnsureModDownloadAccess(string mod) { }

    private static bool LeaveArcaneWars(UserSettings settings) => false;
}
