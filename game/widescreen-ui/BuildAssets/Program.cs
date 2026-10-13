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
        payload.Add((race == "" ? "data" : "skins/" + race) + "/" + entry.ToLowerInvariant().Replace("background.tga", "PawUniformCenter.tga"), center);
        // A strip is a lossless atlas crop: orientation, alpha and every row survive.
        var width = BitConverter.ToUInt16(original, 12);
        var height = BitConverter.ToUInt16(original, 14);
        for (int row = 0; row < height; row++)
        {
            if (!original.AsSpan(18 + original[0] + (row * width + width / 2 - 1) * 4, 8).SequenceEqual(center.AsSpan(18 + row * 8, 8)))
                throw new InvalidDataException("Atlas crop changed pixels.");
            checks++;
        }
        if (center[17] != original[17]) throw new InvalidDataException("TGA orientation changed.");
        checks++;
    }
    if (race != "") continue;
    foreach (var name in new[] { "control_panel", "action_buttons", "object_display" })
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
            // Native tiled decal keeps the original atlas halves at the edges;
            // only its two-column centre repeats across the extra width.
            text = text[..begin] + "\t\t[Child Template=DecalVETiledHorizontal]\n\t\t{\n\t\t\tends_texture = /UI/%s/Game/ControlPanel/Background.tga\n\t\t\tcenter_texture = /UI/%s/Game/ControlPanel/PawUniformCenter.tga\n\t\t}" + text[end..];
            text = ReplaceOnce(text, "L = 799", "L = 225r");
        }
        if (name == "action_buttons") text = ReplaceOnce(text, "L = 800", "L = 224r");
        if (name == "object_display") text = ReplaceOnce(text, "R = +456", "R = 273r");
        payload.Add("data/ui/game/" + name + ".tgi", Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray());
    }
}
if (used.Count != 24 || sourceHashes.Count != 25 || payload.Count != 24)
    throw new InvalidDataException("Expected 21 stock atlases and 3 layouts.");
const string arcaneKey = "ArcaneWars-0.82.1.8-clean.1:data/UI/Game/object_display.tgi";
var arcaneOriginal = File.ReadAllBytes(args[3]);
if (Convert.ToHexString(SHA256.HashData(arcaneOriginal)) != sourceHashes[arcaneKey])
    throw new InvalidDataException("Unrecognized Arcane Wars object display.");
// Arcane Wars has tighter company member spacing. Keep all of its content;
// Vanilla/Immortals continue using the original retail layout above.
var arcaneText = new UTF8Encoding(false, true).GetString(arcaneOriginal);
var arcaneDisplay = ReplaceOnce(arcaneText, "R = +456", "R = 273r");
checks++;
// At the stock logical width these anchors preserve every original coordinate.
// Wider layouts leave a gap between the centre display and the right action panel.
foreach (var width in new[] { 1024d, 1280, 1365.333333, 1536, 1820.444444, 2730.666667 })
{
    var objectRight = width - 273;
    var actionLeft = width - 224;
    if (objectRight - 295 < 456 || objectRight >= actionLeft || actionLeft + 197 > width)
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
Console.WriteLine($"UI_ASSETS_PASS: {checks} checks; 24 assets per family; 7 skins; original pixels, 4:3 anchors and mod-specific spacing preserved.");

static string ReplaceOnce(string text, string from, string to)
{
    var index = text.IndexOf(from, StringComparison.Ordinal);
    if (index < 0 || text.IndexOf(from, index + from.Length, StringComparison.Ordinal) >= 0)
        throw new InvalidDataException("Ambiguous layout anchor: " + from);
    return text[..index] + to + text[(index + from.Length)..];
}

static byte[] CenterStrip(byte[] source)
{
    if (source.Length < 18 || source[1] != 0 || source[2] != 2 || source[16] != 32 || (source[17] & 0xC0) != 0)
        throw new InvalidDataException("Expected non-interleaved 32-bit stock TGA.");
    int width = BitConverter.ToUInt16(source, 12), height = BitConverter.ToUInt16(source, 14), offset = 18 + source[0];
    if (width < 2 || width % 2 != 0 || height == 0 || offset + (long)width * height * 4 > source.Length)
        throw new InvalidDataException("Truncated or invalid TGA.");
    byte[] result = new byte[18 + height * 8];
    Array.Copy(source, result, 18);
    result[0] = 0; result[12] = 2; result[13] = 0;
    for (int row = 0; row < height; row++)
        Array.Copy(source, offset + (row * width + width / 2 - 1) * 4, result, 18 + row * 8, 8);
    return result;
}
