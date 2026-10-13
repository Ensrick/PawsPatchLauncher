using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class IconChecks
{
    internal static void Run(string path)
    {
        var kinds = Enum.GetValues<IconKind>().Where(k => k != IconKind.None).ToArray();
        var shapes = (IReadOnlyDictionary<IconKind, Geometry>)typeof(LauncherIcon)
            .GetField("Shapes", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var pen = new Pen(Brushes.White, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        var errors = new List<string>();
        var checks = 0;
        var gold = new SolidColorBrush(Color.FromRgb(255, 214, 122));
        var gallery = new WrapPanel { Width = 900, Background = new SolidColorBrush(Color.FromRgb(13, 27, 49)) };
        foreach (var kind in kinds)
        {
            checks++;
            if (!shapes.TryGetValue(kind, out var geometry)) { errors.Add("Missing geometry: " + kind); continue; }
            var bounds = kind == IconKind.Discord ? geometry.Bounds : geometry.GetRenderBounds(pen);
            if (!new Rect(0, 0, 24, 24).Contains(bounds)) errors.Add($"{kind}: stroke outside 24-unit canvas: {bounds}");
            foreach (var size in new[] { 15, 18, 19, 21, 24, 32, 42 })
            foreach (var dpi in new[] { 1d, 1.25, 1.5, 2d })
            {
                var host = new Grid { Width = size + 8, Height = size + 8 };
                host.Children.Add(new LauncherIcon { Kind = kind, Width = size, Height = size, Foreground = gold });
                var bitmap = Render(host, size + 8, size + 8, dpi);
                var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
                bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                var visible = 0; var outside = false;
                for (var y = 0; y < bitmap.PixelHeight; y++)
                for (var x = 0; x < bitmap.PixelWidth; x++)
                {
                    if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] <= 8) continue;
                    visible++;
                    outside |= x < Math.Floor(4 * dpi) || y < Math.Floor(4 * dpi)
                        || x >= Math.Ceiling((size + 4) * dpi) || y >= Math.Ceiling((size + 4) * dpi);
                }
                checks++;
                if (visible < 3 || outside) errors.Add($"{kind}: {size}px at {dpi * 100}% is empty or overflows");
            }
            var cell = new StackPanel { Width = 180, Height = 110 };
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Height = 70 };
            foreach (var size in new[] { 18, 21, 42 })
                row.Children.Add(new LauncherIcon { Kind = kind, Width = size, Height = size, Foreground = gold, Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            cell.Children.Add(row);
            cell.Children.Add(new TextBlock { Text = kind.ToString(), Foreground = Brushes.White, FontFamily = new FontFamily("Arial"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
            gallery.Children.Add(cell);
        }
        var image = Render(gallery, 900, (int)Math.Ceiling(kinds.Length / 5d) * 110, 1);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (var output = File.Create(path)) encoder.Save(output);
        foreach (var error in errors) Console.WriteLine(error);
        if (errors.Count > 0) throw new InvalidOperationException($"Icon audit: {errors.Count} failures across {checks} checks; gallery: {path}");
        Console.WriteLine($"ICONS PASS {checks}: {kinds.Length} icons, seven sizes, 100/125/150/200% DPI; no missing or clipped shapes; {path}");
    }

    private static RenderTargetBitmap Render(FrameworkElement element, int width, int height, double dpi)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi), (int)Math.Ceiling(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(element); return bitmap;
    }
}
