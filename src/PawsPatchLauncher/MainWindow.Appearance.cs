using System.Windows;
using System.Windows.Controls;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private void InitializeAppearance()
    {
        var components = (Panel)CoreModuleCard.Parent;
        foreach (var header in new FrameworkElement[] { ModulesHeader, CompatibilityBanner, ModSelectorCard })
        {
            components.Children.Remove(header);
            PinnedComponentsHost.Children.Add(header);
        }
        components.Children.Remove(OosModuleCard);
        components.Children.Insert(components.Children.IndexOf(CoreModuleCard) + 1, OosModuleCard);
        components.Children.Remove(RoamingSpawnCard);
        components.Children.Insert(components.Children.IndexOf(PowersShardsCard) + 1, RoamingSpawnCard);
        SizeChanged += (_, _) => RefreshActionLayout();
        RefreshActionLayout();
        foreach (var (button, kind) in new (Button, IconKind)[]
        {
            (HomeNav, IconKind.Home), (ModulesNav, IconKind.Components), (ModManagerNav, IconKind.Mods), (FriendsNav, IconKind.Multiplayer), (SettingsNav, IconKind.Settings), (AboutNav, IconKind.Help), (AboutModsNav, IconKind.Components),
            (BrowseButton, IconKind.Folder), (OpenGameFolderButton, IconKind.Folder), (OpenSavesFolderButton, IconKind.Folder), (ApplySettingsButton, IconKind.Check), (DiagnosticsButton, IconKind.Diagnostics), (ShowDiagnosticsArchiveButton, IconKind.Folder), (CopyConfigurationButton, IconKind.Copy),
            (CheckUpdatesButton, IconKind.Sync), (LaunchButton, IconKind.Play), (LauncherUpdateButton, IconKind.Download),
            (SettingsRepairButton, IconKind.Shield), (RemovePatchButton, IconKind.Trash), (RemoveLauncherButton, IconKind.Trash)
        }) LauncherIcon.SetKind(button, kind);
        _cleanCache.Click += (_, _) => CardHighlight.Pulse(_storageCard);
        _cleanBackups.Click += (_, _) => CardHighlight.Pulse(_storageCard);
    }

    // Read-only presentation snapshot. Compare accepted settings, not Click/Checked events:
    // initial load, rejected edits and clicking an already-selected mode must not flash.
    private Dictionary<Border, string> CaptureAppearance() => new()
    {
        [CoreModuleCard] = GameMod.PawPatchSelected(_settings).ToString(),
        [RussianModuleCard] = GameLanguages.Text(_settings) + ":" + GameLanguages.Voice(_settings),
        [ColorsModuleCard] = GameMod.ColorsSelected(_settings).ToString(),
        [IndependentHostilityCard] = (GameMod.IsArcaneWars(_settings) ? _settings.IndependentHostility : GameMod.PureComponents(_settings).IndependentHostility).ToString(),
        [AdditionalRoamingCard] = (GameMod.IsArcaneWars(_settings) ? _settings.AdditionalRoamingCompanies : GameMod.PureComponents(_settings).AdditionalRoamingCompanies).ToString(),
        [SiegeBalanceCard] = _settings.SiegeBalance.ToString(),
        [ImprovedAiCard] = GameMod.ImprovedAiSelected(_settings).ToString(),
        [PureLairCard] = GameMod.PureComponents(_settings).WoundedLairDefenders.ToString(),
        [PowersShardsCard] = _settings.DisablePowersAndShards.ToString(),
        [OosModuleCard] = GameMod.DesyncSelected(_settings).ToString(),
        [RoamingSpawnCard] = GameMod.IsArcaneWars(_settings) ? _settings.RoamingSpawnMode : GameMod.PureComponents(_settings).RoamingSpawnMode,
        [SettingsPanel] = _settings.Language,
        [PatchUpdatesCard] = _settings.Channel
    };

    private void HighlightAppearanceChanges(Dictionary<Border, string> before)
    {
        if (_initializing) return;
        foreach (var (card, value) in CaptureAppearance())
            if (before.TryGetValue(card, out var previous) && value != previous) CardHighlight.Pulse(card);
    }
}
