using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private static SolidColorBrush ColorBrush(string color) => new((Color)ColorConverter.ConvertFromString(color));

    private Border RestrictionCard(string message) => new()
    {
        Background = ColorBrush("#3F2530"), BorderBrush = ColorBrush("#B45F68"), BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(9), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 12),
        Child = new TextBlock { Text = message, Foreground = ColorBrush("#FFBDC0"), FontSize = 13, TextWrapping = TextWrapping.Wrap }
    };
}
