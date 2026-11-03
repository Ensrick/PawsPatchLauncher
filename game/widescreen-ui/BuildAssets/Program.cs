using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PawsPatchLauncher.Rwd;

if (args.Length != 4)
    throw new ArgumentException("Usage: BuildAssets <stock game> <source-assets.json> <new output directory> <Arcane Wars original object_display.tgi>");
var sourceHashes = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[1]))!;
var output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Output directory must be new.");
var payload = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
var used = new HashSet<string>(StringComparer.Ordinal);
int checks = 0;
byte[] ReadStock(RwdArchive archive, string archiveName, string entryName)
{
    var key = archiveName + ":" + entryName;
    var bytes = archive.Read(archive.Find(entryName) ?? throw new InvalidDataException(key));
    if (!sourceHashes.TryGetValue(key, out var hash) || Convert.ToHexString(SHA256.HashData(bytes)) != hash)
        throw new InvalidDataException("Unrecognized stock UI asset: " + key);
    used.Add(key); checks++;
    return bytes;
}
foreach (var race in new[] { "", "human", "gauri", "drauga", "haroun", "undead", "shadow" })
{
    var archiveName = race == "" ? "Data.rwd" : "skins/" + race + ".rwd";
    using var archive = RwdArchive.Open(Path.Combine(args[0], archiveName));
    foreach (var variant in new[] { "", "800/", "1280/" })
    {
        var entry = "UI/" + variant + "Game/ControlPanel/Background.tga";
        var original = ReadStock(archive, archiveName, entry);
        var center = CenterStrip(original);
        var prefix = (race == "" ? "data" : "skins/" + race) + "/" + entry.ToLowerInvariant();
        payload.Add(prefix.Replace("background.tga", "PawUniformCenter.tga"), center);
        // A strip is a lossless atlas crop: orientation, alpha and every row survive.
        var width = BitConverter.ToUInt16(original, 12);
        var height = BitConverter.ToUInt16(original, 14);
        var left = CropColumns(original, 0, width / 2);
        var right = CropColumns(original, width / 2, width / 2);
        payload.Add(prefix.Replace("background.tga", "PawUniformLeft.tga"), left);
        payload.Add(prefix.Replace("background.tga", "PawUniformRight.tga"), right);
        for (int row = 0; row < height; row++)
        {
            int stripWidth = width / 8;
            if (!original.AsSpan(18 + original[0] + (row * width + width / 2) * 4, stripWidth * 4).SequenceEqual(center.AsSpan(18 + row * stripWidth * 4, stripWidth * 4)))
                throw new InvalidDataException("Atlas crop changed pixels.");
            var rowBytes = original.AsSpan(18 + original[0] + row * width * 4, width * 4);
            if (!rowBytes[..(width * 2)].SequenceEqual(left.AsSpan(18 + row * width * 2, width * 2))
                || !rowBytes[(width * 2)..].SequenceEqual(right.AsSpan(18 + row * width * 2, width * 2)))
                throw new InvalidDataException("Panel halves changed color or alpha.");
            checks++;
        }
        if (center[17] != original[17]) throw new InvalidDataException("TGA orientation changed.");
        checks++;
    }
    if (race != "") continue;
    foreach (var name in new[] { "control_panel", "action_buttons", "object_display", "time_message" })
    {
        var bytes = ReadStock(archive, archiveName, "UI/Game/" + name + ".tgi");
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, true);
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        if (name == "control_panel")
        {
            const string start = "\t\t[Child Template=DecalVE]";
            var begin = text.IndexOf(start, StringComparison.Ordinal);
            var end = text.IndexOf("\n\t}\n", begin, StringComparison.Ordinal);
            if (begin < 0 || end < begin || !text[begin..end].Contains("texture = /UI/%s/Game/ControlPanel/Background.tga"))
                throw new InvalidDataException("Unexpected stock panel layout.");
            // Native VE picking uses client rectangles, not texture alpha.
            // Keep the full artwork, but restrict all decorative decals to the
            // stock panel client band. Minimap/buttons have independent views.
            text = text[..begin] + """
                    validate_decal_children = false
                    [Child Template=DecalVE]
                    {
                        L = 0
                        R = 512
                        client_T = 183b
                        texture = /UI/%s/Game/ControlPanel/PawUniformLeft.tga
                    }
                    [Child Template=DecalVE]
                    {
                        L = 512
                        R = 512r
                        client_T = 183b
                        tiled_x = true
                        texture = /UI/%s/Game/ControlPanel/PawUniformCenter.tga
                    }
                    [Child Template=DecalVE]
                    {
                        L = 512r
                        R = 100%
                        client_T = 183b
                        texture = /UI/%s/Game/ControlPanel/PawUniformRight.tga
                    }
            """ + text[end..];
            text = ReplaceOnce(text, "L = 799", "L = 225r");
        }
        if (name == "action_buttons") text = ReplaceOnce(text, "L = 800", "L = 224r");
        if (name == "object_display") text = ObjectDisplay(text);
        if (name == "time_message")
        {
            text = text.Replace("L = 495", "L = 0%").Replace("R = +82", "R = 100%").Replace("justify = LEFT", "justify = CENTER");
            // Paw's Patch keeps both time labels directly above the HUD.
            text = ReplaceOnce(text, "T = 10", "T = 208b");
            text = ReplaceOnce(text, "T = 35", "T = 208b");
        }
        payload.Add("data/ui/game/" + name + ".tgi", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray());
    }
}
if (used.Count != 25 || sourceHashes.Count != 26 || payload.Count != 67)
    throw new InvalidDataException("Expected 63 cropped textures and 4 layouts from 21 stock atlases.");
const string arcaneKey = "ArcaneWars-0.82.1.8-clean.1:data/UI/Game/object_display.tgi";
var arcaneOriginal = File.ReadAllBytes(args[3]);
if (Convert.ToHexString(SHA256.HashData(arcaneOriginal)) != sourceHashes[arcaneKey])
    throw new InvalidDataException("Unrecognized Arcane Wars object display.");
// Arcane Wars has tighter company member spacing. Keep all of its content;
// Vanilla/Immortals continue using the original retail layout above.
var arcaneText = new UTF8Encoding(false, true).GetString(arcaneOriginal);
var arcaneDisplay = ObjectDisplay(arcaneText.Replace("\r\n", "\n"));
checks++;
// At the stock logical width these anchors preserve every original coordinate.
// Wider layouts leave a gap between the centre display and the right action panel.
foreach (var width in new[] { 1024d, 1280, 1365.333333, 1536, 1820.444444, 2730.666667 })
{
    var objectLeft = 295d;
    var objectRight = width - 273;
    var actionLeft = width - 224;
    if (objectLeft < 295 || objectRight >= actionLeft || actionLeft + 197 > width)
        throw new InvalidDataException("Overlapping UI anchors.");
    checks++;
}
foreach (var scope in new[] { "pure", "arcane" })
{
    var manifest = new SortedDictionary<string, string>(StringComparer.Ordinal);
    foreach (var pair in payload)
    {
        var bytes = scope == "arcane" && pair.Key == "data/ui/game/object_display.tgi"
            ? Encoding.UTF8.GetBytes(arcaneDisplay) : pair.Value;
        var path = Path.Combine(output, scope, "payload", pair.Key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        manifest.Add(pair.Key, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    File.WriteAllText(Path.Combine(output, scope, "assets.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");
}
Console.WriteLine($"UI_ASSETS_PASS: {checks} checks; 67 assets per family; 7 skins; original pixels/alpha, 4:3 panel anchors and mod-specific spacing preserved.");

static string ReplaceOnce(string text, string from, string to)
{
    var index = text.IndexOf(from, StringComparison.Ordinal);
    if (index < 0 || text.IndexOf(from, index + from.Length, StringComparison.Ordinal) >= 0)
        throw new InvalidDataException("Ambiguous layout anchor: " + from);
    return text[..index] + to + text[(index + from.Length)..];
}

static string ObjectDisplay(string text)
{
    // Expand the input container, not the portraits. The native group-spacing
    // hook keeps the six mini cards within their original 412-unit area.
    text = ReplaceOnce(text, "R = +456", "R = 273r");
    text = ReplaceOnce(text, "L = 333", "L = 123r"); // single company banner
    if (text.Split("L = 336").Length != 3) throw new InvalidDataException("Company banner bars changed.");
    text = text.Replace("L = 336", "L = 120r"); // morale and efficiency
    text = ReplaceOnce(text, "[FormationButtons Template=FormationButtonsTemplate]\n\t}",
        "[FormationButtons Template=FormationButtonsTemplate]\n\t\t{\n\t\t\t[View]\n\t\t\tL = 44r\n\t\t}\n\t}");
    return ReplaceOnce(text, ";L = 410", "L = 44r"); // multiple selection
}

static byte[] CenterStrip(byte[] source)
{
    if (source.Length < 18 || source[1] != 0 || source[2] != 2 || source[16] != 32 || (source[17] & 0xC0) != 0)
        throw new InvalidDataException("Expected non-interleaved 32-bit stock TGA.");
    int width = BitConverter.ToUInt16(source, 12), height = BitConverter.ToUInt16(source, 14), offset = 18 + source[0];
    if (width < 2 || width % 2 != 0 || height == 0 || offset + (long)width * height * 4 > source.Length)
        throw new InvalidDataException("Truncated or invalid TGA.");
    // Repeat a complete decorative segment, rather than two magnified pixels.
    // This interval excludes the minimap and right action-panel ornaments.
    return CropColumns(source, width / 2, width / 8);
}

static byte[] CropColumns(byte[] source, int x, int columns)
{
    int width = BitConverter.ToUInt16(source, 12), height = BitConverter.ToUInt16(source, 14);
    if (x < 0 || columns <= 0 || x + columns > width)
        throw new InvalidDataException("Invalid crop bounds.");
    byte[] result = new byte[18 + height * columns * 4];
    Array.Copy(source, result, 18);
    result[0] = 0;
    result[12] = (byte)columns; result[13] = (byte)(columns >> 8);
    for (int row = 0; row < height; row++)
        Array.Copy(source, 18 + source[0] + (row * width + x) * 4,
            result, 18 + row * columns * 4, columns * 4);
    return result;
}
