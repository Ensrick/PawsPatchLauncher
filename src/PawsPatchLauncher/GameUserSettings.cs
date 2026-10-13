using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PawsPatchLauncher;

public sealed class GameUserSettingsChangedException : IOException;
public sealed class GameUserSettingsRunningException : InvalidOperationException;

/// <summary>Edits only selected user variables, preserving all other bytes.</summary>
public sealed class GameUserSettings
{
    private const int MaximumSize = 1024 * 1024;
    private static readonly Dictionary<string, string> Types = new(StringComparer.Ordinal)
    {
        ["ResolutionX"]="int", ["ResolutionY"]="int", ["FrameRateLimit"]="float",
        ["AudioMainVolume"]="float", ["Audio2DVolume"]="float", ["Audio3DVolume"]="float",
        ["AudioSpeechVolume"]="float", ["AudioMusicVolume"]="float",
        ["ViewShowElapsedGameTime"]="flag", ["MinimapColorByKingdom"]="flag"
    };
    private readonly byte[]? _original;
    private readonly Encoding _encoding;
    private readonly byte[] _preamble;
    private readonly string _text;
    private readonly Group _body;
    private readonly Dictionary<string, Group> _values = new(StringComparer.Ordinal);
    public string Path { get; }
    public bool Exists => _original is not null;
    public static string UserPath => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Kohan2", "data", "User", "UVars.tgi");

    private GameUserSettings(string path, byte[]? original)
    {
        Path = System.IO.Path.GetFullPath(path); _original = original;
        var bytes = original ?? [];
        (_encoding, _preamble) = bytes.AsSpan().StartsWith(new byte[]{0xef,0xbb,0xbf}) ? (new UTF8Encoding(false,true), new byte[]{0xef,0xbb,0xbf})
            : bytes.AsSpan().StartsWith(new byte[]{0xff,0xfe}) ? (new UnicodeEncoding(false,false,true), new byte[]{0xff,0xfe})
            : bytes.AsSpan().StartsWith(new byte[]{0xfe,0xff}) ? (new UnicodeEncoding(true,false,true), new byte[]{0xfe,0xff})
            // All edited tokens are ASCII. Latin1 round-trips BOM-less UTF-8 and
            // legacy game encodings without rewriting comments or other strings.
            : (Encoding.Latin1, Array.Empty<byte>());
        _text = original is null ? "[Vars]\r\n{\r\n}\r\n" : _encoding.GetString(bytes, _preamble.Length, bytes.Length - _preamble.Length);
        var blocks = Regex.Matches(_text, @"(?ms)^[ \t]*\[Vars\][ \t\r\n]*\{(?<body>.*?)^[ \t]*\}", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (blocks.Count != 1 || Regex.IsMatch(blocks[0].Groups["body"].Value, @"(?m)^[ \t]*[\[{}]")) throw new InvalidDataException("UVars.tgi: expected one flat [Vars] block.");
        _body = blocks[0].Groups["body"];
        foreach (var (key, type) in Types)
        {
            var fields = Regex.Matches(_body.Value, @"(?m)^[ \t]*(?<type>\w+)[ \t]+" + key + @"[ \t]*=[ \t]*(?<value>[^\r\n]*?)(?:[ \t]*(?://|;;)[^\r\n]*)?[ \t]*\r?$", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (fields.Count > 1 || fields.Count == 1 && fields[0].Groups["type"].Value != type) throw new InvalidDataException("UVars.tgi: ambiguous variable " + key);
            if (fields.Count == 1) _values[key] = fields[0].Groups["value"];
        }
    }

    public static GameUserSettings Load(string path) => new(path, Read(path));
    private static byte[]? Read(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumSize) throw new InvalidDataException("UVars.tgi is too large.");
            var bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes); return bytes;
        }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public string? Value(string key) => _values.TryGetValue(key, out var value) ? value.Value : null;
    public double? Number(string key)
    {
        var value = Value(key); if (value is null) return null;
        var percent = value.EndsWith('%');
        return double.TryParse(percent ? value[..^1] : value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n / (percent ? 100 : 1) : null;
    }
    public bool? Flag(string key) => bool.TryParse(Value(key), out var value) ? value : null;
    public static void Validate(string key, string value)
    {
        if (!Types.TryGetValue(key, out var type)) throw new ArgumentException("Unsupported game setting.");
        if (type == "flag") { if (value is not ("true" or "false")) throw new ArgumentException("Expected true or false."); return; }
        if (type == "int" && !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _)) throw new ArgumentException("Expected an integer.");
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) throw new ArgumentException("Expected a finite number.");
        if (key == "ResolutionX" ? n != Math.Truncate(n) || n < 800 || n > 16384
            : key == "ResolutionY" ? n != Math.Truncate(n) || n < 600 || n > 16384
            : key == "FrameRateLimit" ? n < 1 || n > 1000 : n < 0 || n > 1) throw new ArgumentOutOfRangeException(key);
    }
    public byte[] Edit(IReadOnlyDictionary<string, string> edits)
    {
        if (edits.ContainsKey("ResolutionX") != edits.ContainsKey("ResolutionY")) throw new ArgumentException("Both resolution dimensions are required.");
        foreach (var (key, value) in edits) Validate(key, value);
        if (edits.Count == 0) return _original?.ToArray() ?? [];
        var body = _body.Value;
        foreach (var (key, value) in edits.Where(e => _values.ContainsKey(e.Key)).OrderByDescending(e => _values[e.Key].Index))
        {
            var old = _values[key]; body = body.Remove(old.Index, old.Length).Insert(old.Index, value);
        }
        var newline = _text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        foreach (var (key, value) in edits.Where(e => !_values.ContainsKey(e.Key)))
            body += (body.EndsWith('\n') ? "" : newline) + "\t" + Types[key] + "\t" + key + " = " + value + newline;
        var updated = _text.Remove(_body.Index, _body.Length).Insert(_body.Index, body);
        return [.._preamble, .._encoding.GetBytes(updated)];
    }

    public string? Save(IReadOnlyDictionary<string, string> edits, Func<bool> gameRunning)
    {
        var updated = Edit(edits);
        if (edits.Count == 0 || _original is not null && updated.AsSpan().SequenceEqual(_original)) return null;
        void CheckCurrent()
        {
            if (gameRunning()) throw new GameUserSettingsRunningException();
            var current = Read(Path);
            if ((_original is null) != (current is null) || _original is not null && !current!.AsSpan().SequenceEqual(_original)) throw new GameUserSettingsChangedException();
        }
        CheckCurrent();
        var directory = System.IO.Path.GetDirectoryName(Path)!; Directory.CreateDirectory(directory);
        var temp = System.IO.Path.Combine(directory, ".paws-uvars-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(updated); stream.Flush(true); }
            CheckCurrent();
            string? backup = null;
            if (_original is null) File.Move(temp, Path);
            else
            {
                var backups = System.IO.Path.Combine(directory, "PawsLauncherBackups"); Directory.CreateDirectory(backups);
                backup = System.IO.Path.Combine(backups, "UVars-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".tgi");
                File.Replace(temp, Path, backup);
            }
            return backup;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
