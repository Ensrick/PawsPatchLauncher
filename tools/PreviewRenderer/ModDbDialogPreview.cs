using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class ModDbDialogPreview
{
    internal static void Run(string language, string output)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Disposable profile required.");
        const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
        var owner = new MainWindow { Width = 1200, Height = 800, Left = -32000, Top = -32000, ShowInTaskbar = false, ShowActivated = false };
        Window? dialog = null;
        try
        {
            owner.Show(); owner.UpdateLayout();
            ((PawsPatchLauncher.Localization)typeof(MainWindow).GetField("_text",flags)!.GetValue(owner)!).SetLanguage(language);
            dialog = (Window)typeof(MainWindow).GetMethod("CreateModDbArchiveDialog",flags)!.Invoke(owner, [new Action<string>(_ => {})])!;
            dialog.WindowStartupLocation = WindowStartupLocation.Manual; dialog.Left = -32000; dialog.Top = -32000; dialog.ShowActivated = false;
            dialog.Show(); dialog.UpdateLayout();
            var content = (FrameworkElement)dialog.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(content);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(output); encoder.Save(file);
            Console.WriteLine($"MODDB DIALOG RENDER {language}: {bitmap.PixelWidth}x{bitmap.PixelHeight} {output}");
        }
        finally { dialog?.Close(); owner.Close(); }
    }
}
