using System.Windows;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private async Task OpenConfirmedLinkAsync(string url)
    {
        if (url != ArcaneWarsDiscordInvite && url != PrivacyPolicyUrl && url != EnsrickGitHubUrl) return;
        if (ModNoticeOverlay.IsVisible) await CloseModNoticeAsync();
        var confirmation = ConfirmActionAsync(T("Открыть в браузере?", "Open in your browser?"),
            url == ArcaneWarsDiscordInvite
                ? T("Откроется приглашение на Discord-сервер сообщества модов Kohan II в вашем браузере.", "The Kohan II mod community's Discord invitation will open in your browser.")
                : url == EnsrickGitHubUrl
                    ? T("Профиль Ensrick на GitHub откроется в вашем браузере.", "Ensrick’s GitHub profile will open in your browser.")
                    : T("Политика конфиденциальности откроется в вашем браузере.", "The privacy policy will open in your browser."),
            T("АДРЕС СТРАНИЦЫ", "PAGE ADDRESS"), url, T("Открыть браузер", "Open browser"));
        if (ConfirmationActive)
        {
            ConfirmationActionIcon.Kind = IconKind.Help;
            LauncherIcon.SetKind(ConfirmationDeleteButton, IconKind.Help);
            ConfirmationDeleteButton.Background = SocialBrush("#5B451D");
            ConfirmationDeleteButton.BorderBrush = SocialBrush("#D6AA45");
            Motion.SetHoverBackground(ConfirmationDeleteButton, SocialBrush("#765B29"));
        }
        if (await confirmation) await _openHelpLink(url);
    }
}
