using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Pass Brand.xaml, output.ico and preview.png.");
        using var source = File.OpenRead(args[0]);
        var resources = (ResourceDictionary)XamlReader.Load(source);
        var image = (DrawingImage)resources["AppMark"];
        byte[] Render(int size)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(image, new Rect(0, 0, size, size));
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = new MemoryStream(); encoder.Save(output); return output.ToArray();
        }
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var frames = sizes.Select(Render).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        using (var writer = new BinaryWriter(File.Create(args[1])))
        {
            writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)sizes.Length);
            uint offset = (uint)(6 + 16 * sizes.Length);
            for (int i = 0; i < sizes.Length; i++)
            {
                writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i])); writer.Write((byte)(sizes[i] == 256 ? 0 : sizes[i]));
                writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
                writer.Write((uint)frames[i].Length); writer.Write(offset); offset += (uint)frames[i].Length;
            }
            foreach (var frame in frames) writer.Write(frame);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
        File.WriteAllBytes(args[2], Render(512));
        using var icon = File.OpenRead(args[1]);
        var decoder = new IconBitmapDecoder(icon, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        if (!decoder.Frames.Select(f => f.PixelWidth).SequenceEqual(sizes)) throw new Exception("ICO frames do not match the render sizes.");
        Console.WriteLine("ICON PASS: " + string.Join(", ", sizes) + " px; transparent 32-bit frames from the shared vector brand.");
    }
}
