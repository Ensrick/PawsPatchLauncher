using System.Windows;
using System.Windows.Controls;
using PawsPatchLauncher;

namespace PreviewRenderer;

// The launcher has no Guide page: Settings is the last navigation entry and stays clear of the launcher footer.
internal static class NavigationLayoutChecks
{
    internal static void Layout(MainWindow window, FrameworkElement content)
    {
        T Named<T>(string name) => (T)window.FindName(name);
        void Require(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        var settings = Named<Button>("SettingsNav");
        var footer = Named<TextBlock>("LauncherVersionLabel");
        Require(window.FindName("AboutNav") is null && window.FindName("AboutPatchPanel") is null, "The Guide page is back.");
        Require(((Panel)settings.Parent).Children.OfType<FrameworkElement>().Last(c => c.Visibility == Visibility.Visible) == settings,
            "Settings must be the last navigation entry.");
        Require(settings.TranslatePoint(new Point(0, settings.ActualHeight), content).Y < footer.TranslatePoint(new Point(), content).Y - 10,
            "Settings navigation overlaps the launcher footer.");
        Console.WriteLine("NAVIGATION LAYOUT PASS: Settings last, no Guide page, footer clearance");
    }
}
