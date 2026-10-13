using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PawsPatchLauncher;

namespace PreviewRenderer;

/// <summary>
/// The icon colour rule with the real App.xaml styles: icons are gold (LauncherIconAccentBrush) on ghost buttons,
/// follow the dark text on gold buttons and the grey text on disabled buttons, and keep a Foreground set on the icon.
/// </summary>
internal static class IconAccentChecks
{
    internal static int Run(List<string> errors)
    {
        if (Application.Current is null) new App().InitializeComponent();
        var resources = Application.Current!.Resources;
        var gold = ((SolidColorBrush)resources["LauncherIconAccentBrush"]).Color;
        var text = ((SolidColorBrush)resources["TextMainBrush"]).Color;
        var dark = Color.FromRgb(0x15, 0x10, 0x09);
        var grey = Color.FromRgb(0x87, 0x94, 0xA7);
        var green = Color.FromRgb(0x76, 0xDA, 0xB0);
        var checks = 0;
        void Expect(string name, FrameworkElement element, Color expected, params Color[] wrong)
        {
            checks++;
            var (match, other) = Count(element, expected, wrong);
            if (match < 12 || other > match / 4) errors.Add($"Icon colour, {name}: {match} pixels of the expected colour, {other} of another");
        }
        Expect("ghost button", Button("GhostButton", true), gold, text);
        Expect("gold button", Button("GoldButton", true), dark, gold);
        Expect("disabled ghost button", Button("GhostButton", false), grey, gold);
        Expect("disabled gold button", Button("GoldButton", false), grey, gold, dark);
        var plain = new Grid { Width = 40, Height = 40, Background = new SolidColorBrush(Color.FromRgb(13, 27, 49)) };
        TextElement.SetForeground(plain, new SolidColorBrush(text));
        plain.Children.Add(new LauncherIcon { Kind = IconKind.Home, Width = 32, Height = 32 });
        Expect("inherited text colour", plain, gold, text);
        var own = new Grid { Width = 40, Height = 40, Background = new SolidColorBrush(Color.FromRgb(13, 27, 49)) };
        own.Children.Add(new LauncherIcon { Kind = IconKind.Check, Width = 32, Height = 32, Foreground = new SolidColorBrush(green) });
        Expect("icon's own Foreground", own, green, gold);
        var optOut = new Grid { Width = 40, Height = 40, Background = new SolidColorBrush(Color.FromRgb(13, 27, 49)) };
        TextElement.SetForeground(optOut, new SolidColorBrush(text));
        LauncherIcon.SetUseAccent(optOut, false);
        optOut.Children.Add(new LauncherIcon { Kind = IconKind.Home, Width = 32, Height = 32 });
        Expect("UseAccent false on an ancestor", optOut, text, gold);
        return checks;
    }

    private static Button Button(string style, bool enabled)
    {
        var button = new Button { Style = (Style)Application.Current.FindResource(style), Content = "Folder", IsEnabled = enabled, Width = 160, Height = 44 };
        LauncherIcon.SetKind(button, IconKind.Folder);
        return button;
    }

    // Opaque pixels near the expected colour against pixels near any wrong one (distance below 40 in RGB).
    private static (int Match, int Other) Count(FrameworkElement element, Color expected, Color[] wrong)
    {
        element.Measure(new Size(element.Width, element.Height));
        element.Arrange(new Rect(0, 0, element.Width, element.Height));
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)element.Width * 2, (int)element.Height * 2, 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int match = 0, other = 0;
        // on a button only the template's ActionIcon is sampled, not the label next to it
        var area = new Rect(0, 0, element.Width, element.Height);
        if (element is Button button && button.Template.FindName("ActionIcon", button) is FrameworkElement icon)
            area = icon.TransformToAncestor(button).TransformBounds(new Rect(0, 0, icon.ActualWidth, icon.ActualHeight));
        for (var y = (int)(area.Top * 2); y < Math.Min(bitmap.PixelHeight, (int)Math.Ceiling(area.Bottom * 2)); y++)
        for (var x = (int)(area.Left * 2); x < Math.Min(bitmap.PixelWidth, (int)Math.Ceiling(area.Right * 2)); x++)
        {
            var i = (y * bitmap.PixelWidth + x) * 4;
            if (pixels[i + 3] < 250) continue;
            var c = Color.FromRgb(pixels[i + 2], pixels[i + 1], pixels[i]);
            if (Near(c, expected)) match++;
            else if (wrong.Any(w => Near(c, w))) other++;
        }
        return (match, other);
    }

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) < 40;
}
