namespace PawsPatchLauncher;

// Arcane Wars is distributed only to signed-in members of its publisher's team until its author approves
// public access. This launcher has no accounts, so the mod can never be selected, installed or launched here.
public partial class MainWindow
{
    private bool ArcaneAccessBlocked => GameMod.IsArcaneWars(_settings);
    private string ArcaneAccessReason => T("Arcane Wars недоступен в K2 Launcher. Выберите Vanilla или Immortals.",
        "Arcane Wars is not available in K2 Launcher. Choose Vanilla or Immortals.");

    private void EnsureModAccess(string mod)
    {
        if (mod == GameMod.ArcaneWars) throw new InvalidOperationException(ArcaneAccessReason);
    }

    private void EnsureModDownloadAccess(string mod) => EnsureModAccess(mod);

    // Settings carried over from another launcher may still name Arcane Wars.
    private static bool LeaveArcaneWars(UserSettings settings)
    {
        if (!GameMod.IsArcaneWars(settings)) return false;
        ModChannelSelection.SelectMod(settings, GameMod.Vanilla);
        return true;
    }
}
