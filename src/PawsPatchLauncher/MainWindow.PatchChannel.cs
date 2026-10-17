using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private bool _syncingPatchChannel;
    private readonly Dictionary<string, ChannelManifest?> _localChannelGuides = new();
    private ChannelManifest? ChannelForMod(string channel) => _feedClient.KnownChannel(channel)
        ?? (_latestChannel?.Channel == channel ? _latestChannel : _channel?.Channel == channel ? _channel : LocalChannelGuide(channel));
    private bool SelectedModHasBeta => ModChannelSelection.HasDistinctBeta(_settings.Mod, ChannelForMod("stable"), ChannelForMod("beta"));

    private ChannelManifest? LocalChannelGuide(string channel)
    {
        if (!_localChannelGuides.ContainsKey(channel)) _localChannelGuides[channel] = _feedClient.CachedGuideChannel(channel, null);
        return _localChannelGuides[channel];
    }

    private void SyncPatchChannelControls()
    {
        _syncingPatchChannel = true;
        try
        {
            var beta = _settings.Channel == "beta";
            ModReleaseRadio.IsChecked = !beta;
            ModBetaRadio.IsChecked = beta;
            ModReleaseRadio.IsEnabled = !_busy && !FeedBlocksActions;
            ModBetaRadio.IsEnabled = !_busy && !FeedBlocksActions && (SelectedModHasBeta || beta);
            ModBetaRadio.ToolTip = SelectedModHasBeta || beta ? _text["patch.beta.tip"] : T("Отдельная бета для этого режима пока недоступна.", "A separate Beta is not yet available for this mode.");
            BetaDetailsButton.IsEnabled = !_busy && (SelectedModHasBeta || beta);
            var version = ModPatchVersion(_channel, _settings.Mod);
            ModPatchVersionText.Text = "Paw's Patch " + (version ?? T("— нет данных о версии", "— version unavailable"))
                + " · " + ChannelPresentation.Name(_settings.Channel, _text.Language)
                + (GameMod.PawPatchSelected(_settings) ? "" : T(" · выключен", " · off"));
        }
        finally { _syncingPatchChannel = false; }
        PatchChannelCard.ToolTip = ModPatchVersionText.Text;
    }

    private static string? ModPatchVersion(ChannelManifest? channel, string mod) => PawPatchVersions.ForChannel(channel, mod);

    private void ApplyPatchChannelLanguage()
    {
        PatchUpdatesTitleText.Text = _text["patch.updates"];
        PatchChannelDescriptionText.Text = T("Канал Paw's Patch выбирается в компонентах и запоминается отдельно для каждого мода.",
            "Choose the Paw's Patch channel in Components. Each mod remembers its own selection.");
        LauncherUpdatesDescriptionText.Text = _text["launcher.updates.help"];
        ModReleaseRadio.Content = ChannelPresentation.Name("stable", _text.Language);
        ModBetaRadio.Content = ChannelPresentation.Name("beta", _text.Language);
        PatchChannelLabelText.Text = T("Канал", "Channel");
        BetaDetailsButton.ToolTip = T("Отличия беты", "Beta differences");
        ModReleaseRadio.ToolTip = _text["patch.release.tip"];
        foreach (var control in new[] { ModReleaseRadio, ModBetaRadio })
            System.Windows.Automation.AutomationProperties.SetName(control,
                GameMod.Name(_settings.Mod, _text.Language == "ru") + " · Paw's Patch · " + control.Content);
        SyncPatchChannelControls();
    }

    private async void PatchChannel_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingPatchChannel || _initializing || sender is not RadioButton { Tag: string channel }) return;
        await ChangeChannelAsync(channel == "beta");
    }

    private void BetaDetails_Click(object sender, RoutedEventArgs e) => ShowBetaDetails();

    private void ShowBetaDetails()
    {
        if (ConfirmationActive) return;
        _helpPreviousFocus = System.Windows.Input.Keyboard.FocusedElement ?? FocusManager.GetFocusedElement(this);
        HelpTitleText.Text = GameMod.Name(_settings.Mod, _text.Language == "ru") + " · Paw's Patch · " + T("Бета", "Beta");
        var feed = ChannelForMod("beta");
        var entries = feed?.ModGuides.FirstOrDefault(g => g.Id == _settings.Mod)?.PatchGuide?.Entries.Where(e => e.Category == "beta").ToArray() ?? [];
        HelpBodyText.Text = T("Тестовая ветка Paw's Patch для этого режима. Для совместной игры участникам нужна совместимая версия и конфигурация. Переключение вступит в силу после установки или применения настроек.",
            "The test branch of Paw's Patch for this mode. Multiplayer participants need a compatible version and configuration. Switching takes effect after installation or applying settings.")
            + "\n\n" + (entries.Length > 0 ? string.Join("\n\n", entries.Select(e => e.Title(_text.Language) + "\n" + e.Body(_text.Language)))
                : T("Подробности выбранного выпуска доступны в справке и истории изменений.", "See the guide and changelog for details of the selected release."));
        Motion.Reveal(HelpOverlay);
        RevealDialogCard(HelpCard);
        HelpCloseButton.Focus();
    }
}
