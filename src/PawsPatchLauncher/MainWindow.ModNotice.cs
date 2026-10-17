using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private string _aboutMod = GameMod.ArcaneWars;
    private bool _modNoticeClosing;
    private IInputElement? _modNoticePreviousFocus;

    private void RefreshModNoticeLanguage()
    {
        AboutModsNav.Content = AboutModsTitleText.Text = T("О модах", "About mods");
        ModNoticeTitleText.Text = T("Моды и их авторы", "Mods and their authors");
        ModNoticeBodyText.Text = T(
            "Immortals и Arcane Wars — самостоятельные моды других авторов. Они не являются частью Paw's Patch. Лаунчер помогает устанавливать и переключать моды, а Paw's Patch добавляет собственные изменения поверх выбранного мода.",
            "Immortals and Arcane Wars are independent mods by other authors. They are not part of Paw's Patch. The launcher helps install and switch mods, while Paw's Patch adds its own changes on top of the selected mod.");
        ImmortalsAuthorText.Text = T("Автор: ", "Author: ") + "MartialDoctor";
        ModNoticeArcaneAuthorText.Text = T("Автор: ", "Author: ") + "Darquan Mortis";
        ModsDistributionText.Text = T("Авторы распространяют оба мода на общем Discord-сервере:",
            "The authors distribute both mods on the same Discord server:");
        ModNoticeCloseButton.Content = T("Понятно", "Got it");
        AutomationProperties.SetName(ModNoticeCard, ModNoticeTitleText.Text);
        RefreshModNoticeBadge();
        RefreshAboutMods();
    }

    private void RefreshModNoticeBadge()
    {
        ModulesNavBadge.Visibility = _settings.ModNoticeSeen && !CompatibilityUnread ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetHelpText(ModulesNav, CompatibilityUnread ? T("Некоторые функции патча недоступны", "Some patch features are unavailable") : _settings.ModNoticeSeen ? "" :
            T("Новое: информация об авторах модов", "New: information about mod authors"));
    }

    private void VisitComponents()
    {
        if (!_initializing && !ConfirmationActive) MarkCompatibilityNoticeRead();
        if (_settings.ModNoticeSeen || _initializing || ConfirmationActive) return;
        ShowModNotice();
        _settings.ModNoticeSeen = true;
        try { _settingsStore.Save(_settings); }
        catch (Exception error) { ActivityStore.Log(error); ShowToast(() => T("Не удалось сохранить настройки лаунчера.", "Could not save launcher settings."), true); }
        RefreshModNoticeBadge();
    }


    private void ShowModNotice()
    {
        if (ConfirmationActive || _modNoticeClosing || ModNoticeOverlay.Visibility == Visibility.Visible || _headless) return;
        _modNoticePreviousFocus = Keyboard.FocusedElement;
        ModNoticeLinkErrorText.Visibility = Visibility.Collapsed;
        Motion.Collapse(HelpOverlay);
        MainBody.IsHitTestVisible = false;
        Motion.Reveal(ModNoticeOverlay);
        RevealDialogCard(ModNoticeCard);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (ModNoticeOverlay.Visibility == Visibility.Visible && !_modNoticeClosing) ModNoticeCloseButton.Focus();
        }));
    }

    private async Task CloseModNoticeAsync()
    {
        if (_modNoticeClosing || ModNoticeOverlay.Visibility != Visibility.Visible) return;
        _modNoticeClosing = true;
        await Motion.HideAsync(ModNoticeOverlay);
        MainBody.IsHitTestVisible = !ConfirmationActive;
        _modNoticeClosing = false;
        if (IsLoaded && _modNoticePreviousFocus is UIElement { IsVisible: true, IsEnabled: true } previous) previous.Focus();
        else if (IsLoaded && ModulesNav.IsVisible) ModulesNav.Focus();
        _modNoticePreviousFocus = null;
        RenderCompatibility();
    }

    private async void ModNoticeClose_Click(object sender, RoutedEventArgs e) => await CloseModNoticeAsync();
    private async void ModNotice_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        await CloseModNoticeAsync();
    }
    private async void ModNotice_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (InsideCard(e.OriginalSource as DependencyObject, ModNoticeCard)) return;
        e.Handled = true;
        await CloseModNoticeAsync();
    }

    private async void ModsDiscord_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        if (_openingHelpLink || !e.Uri.IsAbsoluteUri || e.Uri.AbsoluteUri != ArcaneWarsDiscordInvite) return;
        _openingHelpLink = true;
        ModsDiscordLink.IsEnabled = false;
        ModNoticeLinkErrorText.Visibility = Visibility.Collapsed;
        try { await OpenConfirmedLinkAsync(ArcaneWarsDiscordInvite); }
        catch (Exception error)
        {
            ActivityStore.Log(error);
            ModNoticeLinkErrorText.Text = BrowserOpenError();
            ModNoticeLinkErrorText.Visibility = Visibility.Visible;
            ShowToast(BrowserOpenError, true);
        }
        finally { _openingHelpLink = false; ModsDiscordLink.IsEnabled = true; }
    }

    private void AboutModsNav_Click(object sender, RoutedEventArgs e) => SetActivePage("mods");
    private void AboutMod_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mod } || mod == _aboutMod || mod is not ("immortals" or GameMod.ArcaneWars)) return;
        _aboutMod = mod;
        RefreshAboutMods();
        Motion.Reveal(AboutModContentCard);
    }

    private void RefreshAboutMods()
    {
        foreach (var tab in new[] { AboutImmortalsTab, AboutArcaneWarsTab })
        {
            SetChangelogTabState(tab, (string)tab.Tag == _aboutMod);
            AutomationProperties.SetName(tab, (string)tab.Content);
        }
        AboutModNameText.Text = _aboutMod == "immortals" ? "Immortals" : "Arcane Wars";
        AboutModEmptyText.Text = T("Описание мода появится здесь позже.", "The mod description will be added here later.");
    }
}
