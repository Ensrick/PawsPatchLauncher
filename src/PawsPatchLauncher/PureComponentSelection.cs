namespace PawsPatchLauncher;

/// <summary>Independent preferences for the optional Vanilla/Immortals features.</summary>
public sealed class PureComponentSelection
{
    public bool Colors { get; set; }
    public bool IgnoreDesync { get; set; }
    public bool? SuspendedColors { get; set; }
    public bool? SuspendedDesync { get; set; }
    // Remembered independently for each mode; activated only by supporting feeds.
    public bool ImprovedAi { get; set; } = true;
    public bool WoundedLairDefenders { get; set; } = true;
    public bool IndependentHostility { get; set; }
    public bool AdditionalRoamingCompanies { get; set; }
    public string RoamingSpawnMode { get; set; } = "standard";

    public void Enable(bool enabled)
    {
        if (!enabled)
        {
            SuspendedColors ??= Colors;
            SuspendedDesync ??= IgnoreDesync;
            Colors = IgnoreDesync = false;
        }
        else
        {
            Colors = SuspendedColors ?? Colors;
            IgnoreDesync = SuspendedDesync ?? IgnoreDesync;
            SuspendedColors = SuspendedDesync = null;
        }
    }
}
