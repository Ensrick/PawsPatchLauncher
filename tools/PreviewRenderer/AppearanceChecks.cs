using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class AppearanceChecks
{
    internal static void Run(string language, string previewPath)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Appearance checks require --smoke-test.");
        var window = new MainWindow { Left = -32000, Top = -32000, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 1050, Height = 680 };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Invoke(string name, params object?[] values) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(window, values);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, flags)!.GetValue(window)!;
        T Control<T>(string name) => (T)window.FindName(name);
        CardHighlight[] Pulses(Border card) => AdornerLayer.GetAdornerLayer(card)?.GetAdorners(card)?.OfType<CardHighlight>().ToArray() ?? [];
        int tests = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); tests++; }
        async Task Scenario()
        {
            Field<UserSettings>("_settings").Language = language;
            Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language); Invoke("ApplyLanguage");
            Invoke("SetActivePage", "modules"); window.UpdateLayout(); await Task.Delay(240);
            var card = Control<Border>("RussianModuleCard");
            var other = Control<Border>("CoreModuleCard");
            var toggle = Control<CheckBox>("RussianToggle");
            var settings = Field<UserSettings>("_settings");
            Check(Pulses(card).Length == 0, "Initial layout flashed a card.");
            var outline = card.BorderBrush; var fill = card.Background;
            var beforeSize = card.RenderSize;
            var moving = SystemParameters.ClientAreaAnimation;
            var previous = settings.RussianLocalization;
            toggle.IsChecked = !previous; toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(settings.RussianLocalization != previous, "Animation delayed a saved setting.");
            await Task.Delay(55);
            if (moving)
            {
                Check(Pulses(card).Length == 1 && Pulses(card)[0].Opacity is > 0 and < 1, "Changed card has no intermediate highlight.");
                Check(!Pulses(card)[0].IsHitTestVisible, "Highlight intercepts input.");
                Render((FrameworkElement)window.Content, previewPath + ".pulse.png", 1050, 680);
                var currentOpacity = Pulses(card)[0].Opacity;
                CardHighlight.Pulse(card);
                Check(Pulses(card).Length == 1 && Math.Abs(Pulses(card)[0].Opacity - currentOpacity) < .03, "Restart jumped or stacked highlights.");
            }
            Check(Pulses(other).Length == 0, "Unchanged card flashed.");
            Check(ReferenceEquals(outline, card.BorderBrush) && ReferenceEquals(fill, card.Background) && card.RenderSize == beforeSize, "Highlight mutated style brushes or layout.");
            await Task.Delay(850);
            Check(Pulses(card).Length == 0, "Highlight did not remove itself.");
            // An unchanged setting/event must not flash again.
            toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check(Pulses(card).Length == 0, "No-op click flashed a card.");
            for (int i = 0; i < 6; i++) { CardHighlight.Pulse(card); await Task.Delay(15); }
            Check(Pulses(card).Length <= 1, "Rapid changes accumulated adorners.");
            Invoke("SetActivePage", "settings"); await Task.Delay(800);
            Check(Pulses(card).Length == 0, "Hidden page retained a highlight.");
            CardHighlight.Pulse(card);
            Check(Pulses(card).Length == 0, "Hidden card queued an animation.");
            var panel = Control<Border>("SettingsPanel");
            Invoke("LanguageButton_Click", window, new RoutedEventArgs()); await Task.Delay(50);
            Check(!moving || Pulses(panel).Length == 1, "Language setting did not highlight its card.");
            await Task.Delay(800);
            var nav = Control<Button>("ModManagerNav");
            var icon = (LauncherIcon)nav.Template.FindName("ActionIcon", nav);
            Check(icon.Kind == IconKind.Mods && icon.ActualWidth == 18 && icon.IsVisible, "Navigation vector icon missing after language change.");
            Check(nav.Content is string label && !label.Contains('⚔'), "Navigation still relies on font symbols.");
            Check(!icon.IsHitTestVisible && !icon.Focusable, "Decorative icon takes input/focus.");
            Check(!Control<Button>("LanguageButton").IsVisible && Control<ComboBox>("LauncherLanguageCombo").IsVisible,
                "Launcher language must use the settings selector, not the removed header button.");
            Invoke("SetActivePage", "modules"); window.UpdateLayout(); await Task.Delay(240);
            CardHighlight.Pulse(card); window.Content = null; await Task.Delay(40);
            Check(Pulses(card).Length == 0, "Unloaded card leaked an adorner.");

            // Every icon must produce visible strokes, including on the VM with different fonts.
            var gallery = new WrapPanel { Width = 640, Background = new SolidColorBrush(Color.FromRgb(13, 27, 49)) };
            foreach (var kind in Enum.GetValues<IconKind>().Where(k => k != IconKind.None))
            {
                var sample = new LauncherIcon { Kind = kind, Width = 32, Height = 32, Foreground = Brushes.Gold };
                sample.Measure(new Size(32, 32)); sample.Arrange(new Rect(0, 0, 32, 32));
                var bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32); bitmap.Render(sample);
                byte[] pixels = new byte[32 * 32 * 4]; bitmap.CopyPixels(pixels, 128, 0);
                Check(Enumerable.Range(0, 1024).Count(i => pixels[i * 4 + 3] != 0) > 15, "Empty icon: " + kind);
                var cell = new StackPanel { Width = 128, Height = 78, Margin = new Thickness(0, 8, 0, 0) };
                cell.Children.Add(sample); cell.Children.Add(new TextBlock { Text = kind.ToString(), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) });
                gallery.Children.Add(cell);
            }
            Render(gallery, previewPath + ".icons.png", 640, 520);
            Console.WriteLine($"APPEARANCE PASS {tests} {language}: vectors, saved-setting feedback, no-op/hidden/unload, reversal, isolation, no layout/input changes; Windows animations={moving}");
        }
        try
        {
            window.Show();
            var work = window.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();
            var deadline = DateTime.UtcNow.AddSeconds(20);
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(20) };
            timer.Tick += (_, _) => { if (work.IsCompleted || DateTime.UtcNow >= deadline) frame.Continue = false; };
            timer.Start();
            try { Dispatcher.PushFrame(frame); } finally { timer.Stop(); }
            if (!work.IsCompleted) throw new TimeoutException("Appearance UI checks did not finish.");
            work.GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    }

    private static void Render(FrameworkElement view, string path, int width, int height)
    {
        if (view.IsLoaded)
        {
            // Do not resize the live window content independently from its adorner layer.
            view.UpdateLayout(); width = (int)Math.Ceiling(view.RenderSize.Width); height = (int)Math.Ceiling(view.RenderSize.Height);
        }
        else { view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout(); }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var output = File.Create(path); encoder.Save(output);
    }
}
