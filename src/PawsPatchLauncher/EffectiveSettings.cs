using System.Text.Json;

namespace PawsPatchLauncher;

/// <summary>A detached active configuration, not the user's remembered channel preferences.</summary>
public static class EffectiveSettings
{
    public static UserSettings ForChannel(UserSettings preferences, bool colorsAvailable = true)
    {
        var active = JsonSerializer.Deserialize(JsonSerializer.Serialize(preferences, LauncherJsonContext.Default.UserSettings), LauncherJsonContext.Default.UserSettings)!;
        GameMod.Validate(active);
        if (GameMod.IsArcaneWars(active)) active.PureBetaFeatures = false;
        GameLanguages.SetText(active, GameLanguages.Text(active));
        active.SuspendedArcaneComponents = null;
        active.CustomPlayerColors = preferences.CustomPlayerColors && colorsAvailable;
        active.LargeMapSizes = active.PawPatchEnabled;
        active.ImprovedAi &= GameMod.IsArcaneWars(active) && active.PawPatchEnabled && !active.DataOnly;
        if (GameMod.IsArcaneWars(active) && !active.PawPatchEnabled) GameMod.DisableArcaneComponents(active);
        if (active.DataOnly)
        {
            active.CustomPlayerColors = active.IndependentHostility = false;
            active.DesyncMode = "official";
        }
        if (!GameMod.IsArcaneWars(active))
        {
            // Language belongs to the game installation and survives every mod/channel switch.
            active.PawPatchEnabled = GameMod.PawPatchSelected(preferences);
            active.DataOnly &= active.PawPatchEnabled || active.Mod == GameMod.Immortals;
            var options = GameMod.PureComponents(active);
            var enabled = active.PawPatchEnabled && !active.DataOnly;
            options.Colors &= enabled && colorsAvailable;
            options.IgnoreDesync &= enabled;
            options.SuspendedColors = options.SuspendedDesync = null;
            active.CustomPlayerColors = options.Colors;
            active.IndependentHostility = active.AdditionalRoamingCompanies = active.SiegeBalance = false;
            active.DisablePowersAndShards = active.LargeMapSizes = false;
            active.RoamingSpawnMode = "standard";
            active.DesyncMode = options.IgnoreDesync ? "continue" : "official";
            active.PureBetaFeatures &= enabled;
            if (active.PureBetaFeatures)
            {
                active.ImprovedAi = options.ImprovedAi;
                active.IndependentHostility = options.IndependentHostility;
                active.AdditionalRoamingCompanies = options.AdditionalRoamingCompanies;
                active.RoamingSpawnMode = options.RoamingSpawnMode is "x2" or "x4" ? options.RoamingSpawnMode : "standard";
                active.LargeMapSizes = true;
            }
        }
        return active;
    }

    public static UserSettings ForFeed(UserSettings preferences, ChannelManifest? feed)
    {
        var active = ForChannel(preferences, feed is not null && feed.Channel.Equals(preferences.Channel, StringComparison.OrdinalIgnoreCase)
            && (GameMod.IsArcaneWars(preferences) ? feed.Packages.Any(p => p.Id.Equals("player-colors", StringComparison.OrdinalIgnoreCase)) : GameMod.HasPureOptions(feed)));
        var pureBeta = feed?.Channel == active.Channel && GameMod.HasPureBeta(feed);
        active.PureBetaFeatures = !GameMod.IsArcaneWars(active) && pureBeta && active.PawPatchEnabled && !active.DataOnly;
        if (active.PureBetaFeatures)
        {
            var options = GameMod.PureComponents(active);
            if (feed!.PureLairRecoveryBuiltIn) options.WoundedLairDefenders = true;
            active.ImprovedAi = options.ImprovedAi;
            active.IndependentHostility = options.IndependentHostility;
            active.AdditionalRoamingCompanies = options.AdditionalRoamingCompanies;
            active.RoamingSpawnMode = options.RoamingSpawnMode is "x2" or "x4" ? options.RoamingSpawnMode : "standard";
            active.LargeMapSizes = true;
        }
        active.ImprovedAi &= GameMod.HasImprovedAi(feed, active.Mod);
        if (!GameMod.IsArcaneWars(active) && !active.PureBetaFeatures)
        {
            active.ImprovedAi = active.IndependentHostility = active.AdditionalRoamingCompanies = active.LargeMapSizes = false;
            active.RoamingSpawnMode = "standard";
        }
        if (!GameMod.IsArcaneWars(active) && !GameMod.HasPureOptions(feed))
        {
            GameMod.PureComponents(active).IgnoreDesync = false;
            active.DesyncMode = "official";
        }
        if (!GameMod.IsArcaneWars(active) && !GameMod.HasPureFixes(feed))
        {
            GameMod.SetPawPatch(active, false);
            active.PawPatchEnabled = false;
        }
        return active;
    }
}
