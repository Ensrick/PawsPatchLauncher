using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace PawsPatchLauncher;

public partial class MainWindow
{
    private Window? _historyWindow;

    private void PagesWorkspace_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshActionLayout();

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (ConfirmationActive) return;
        var (window, body) = CreateDialog(T("История изменений", "Release history"), IconKind.ReleaseNotes, 840, 840);
        _historyWindow = window;
        ChangelogCard.Margin = new Thickness(0); ChangelogCard.Padding = new Thickness(16);
        NewsTitleText.Visibility = Visibility.Collapsed; body.Content = ChangelogCard;
        window.ContentRendered += (_, _) => { RefreshNews(); MarkVisibleChangelogRead(); };
        window.Activated += (_, _) => MarkVisibleChangelogRead();
        try { window.ShowDialog(); }
        finally { body.Content = null; ChangelogCard.ClearValue(Border.PaddingProperty); NewsTitleText.Visibility = Visibility.Visible;
            _historyWindow = null; CancelChangelogTransition(); HistoryButton.Focus(); }
    }

    // Native resizing/window management with launcher-colored content and caption.
    private (Window Window, ContentControl Body) CreateDialog(string title, IconKind icon, double width, double height)
    {
        var window = new Window { Owner = this, Title = title, Width = Math.Min(width, ActualWidth - 48), Height = Math.Min(height, ActualHeight - 48),
            MinWidth = 360, MinHeight = 300, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.CanResize, Background = ColorBrush("#0A1729"),
            Foreground = ColorBrush("#E8EDF5"), FontFamily = FontFamily, Resources = Resources, ShowInTaskbar = false };
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 60, ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(8), UseAeroCaptionButtons = false });
        var layout = new Grid(); layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        var header = new Grid { Margin = new Thickness(20, 12, 12, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new LauncherIcon { Kind = icon, Width = 23, Height = 23, Foreground = ColorBrush("#D3AF59"), Margin = new Thickness(0, 0, 12, 0) });
        var heading = new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(heading, 1); header.Children.Add(heading);
        var close = new Button { Content = new LauncherIcon { Kind = IconKind.Close, Width = 18, Height = 18 }, Width = 34, Height = 34,
            Padding = new Thickness(0), Style = (Style)FindResource("GhostButton"), ToolTip = T("Закрыть", "Close") };
        AutomationProperties.SetName(close, close.ToolTip.ToString()); WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => window.Close(); Grid.SetColumn(close, 2); header.Children.Add(close); layout.Children.Add(header);
        var body = new ContentControl { Margin = new Thickness(20, 0, 20, 20), HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        window.Content = new Border { Background = ColorBrush("#0A1729"), BorderBrush = ColorBrush("#3B526F"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = layout };
        window.PreviewKeyDown += (_, key) => { if (key.Key == Key.Escape) { key.Handled = true; window.Close(); } };
        return (window, body);
    }
}
