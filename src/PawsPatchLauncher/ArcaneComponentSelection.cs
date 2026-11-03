namespace PawsPatchLauncher;

/// <summary>Local preferences suspended while the Arcane Wars core is off.</summary>
public sealed class ArcaneComponentSelection
{
    public bool CustomPlayerColors { get; set; }
    public bool IndependentHostility { get; set; }
    public bool AdditionalRoamingCompanies { get; set; }
    public bool SiegeBalance { get; set; }
    public bool IgnoreZeroMorale { get; set; }
    public bool ImprovedAi { get; set; } = true;
    public bool DisablePowersAndShards { get; set; }
    public string DesyncMode { get; set; } = "official";
    public string RoamingSpawnMode { get; set; } = "standard";

    public static ArcaneComponentSelection Capture(UserSettings settings) => new()
    {
        CustomPlayerColors = settings.CustomPlayerColors,
        IndependentHostility = settings.IndependentHostility,
        AdditionalRoamingCompanies = settings.AdditionalRoamingCompanies,
        SiegeBalance = settings.SiegeBalance,
        IgnoreZeroMorale = settings.IgnoreZeroMorale,
        ImprovedAi = settings.ImprovedAi,
        DisablePowersAndShards = settings.DisablePowersAndShards,
        DesyncMode = settings.DesyncMode == "continue" ? "continue" : "official",
        RoamingSpawnMode = settings.RoamingSpawnMode is "x2" or "x4" ? settings.RoamingSpawnMode : "standard"
    };

    public void Restore(UserSettings settings)
    {
        settings.CustomPlayerColors = CustomPlayerColors;
        settings.IndependentHostility = IndependentHostility;
        settings.AdditionalRoamingCompanies = AdditionalRoamingCompanies;
        settings.SiegeBalance = SiegeBalance;
        settings.IgnoreZeroMorale = IgnoreZeroMorale;
        settings.ImprovedAi = ImprovedAi;
        settings.DisablePowersAndShards = DisablePowersAndShards;
        settings.DesyncMode = DesyncMode == "continue" ? "continue" : "official";
        settings.RoamingSpawnMode = RoamingSpawnMode is "x2" or "x4" ? RoamingSpawnMode : "standard";
    }
}
