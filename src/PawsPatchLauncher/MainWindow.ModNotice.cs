using System.Windows;
using System.Windows.Automation;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private void RefreshModNoticeBadge()
    {
        ModulesNavBadge.Visibility = CompatibilityUnread ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(ModulesNav, CompatibilityUnread ? T("Некоторые функции патча недоступны", "Some patch features are unavailable") : "");
    }

    private void VisitComponents()
    {
        if (!_initializing && !ConfirmationActive) MarkCompatibilityNoticeRead();
    }
}
