using System.Security.Cryptography;
using System.Text;

namespace PawsPatchLauncher.Rwd;

/// <summary>One file in an .rwd archive. Offset is relative to the Files section.</summary>
public sealed record RwdEntry(string Name, long Offset, long Size, int LoadIndex);

/// <summary>
/// Kohan II .rwd resource archive ("TGCK"), little-endian. Layout as read from the retail Data.rwd and
/// Music.rwd and written by this class:
///   "TGCK", u32 version (2), u32 section count (3)
///   sections, each at the offset its table record gives:
///     Header: u32 2, u16 n + UTF-16 game id (n chars, "K2"), u32 0, u32 stamp (the last 4 bytes)
///     Files:  every file's bytes, back to back
///     Footer: u32 count; per file: u16 n + UTF-16 name (n chars), u64 offset (from the Files section),
///             u64 size, u32 0 (meaning unknown; other values are rejected), u32 load index
///   section table, the last 96 * count bytes of the file; per section: UTF-16 name in 64 bytes (NUL padded),
///     u64 offset, u64 size, u32, u32, u64 size
/// The reader checks every offset and size against the file before use and rejects unsafe names, so a damaged or
/// hostile archive fails with InvalidDataException instead of reading or writing out of bounds.
/// </summary>
public sealed class RwdArchive : IDisposable
{
    public const int MaxEntries = 1_000_000;
    public const int MaxNameLength = 1024;
    const int SectionRecord = 96;
    const int MaxSections = 16;

    readonly FileStream stream;
    readonly long filesOffset;
    readonly Dictionary<string, RwdEntry> byName = new(StringComparer.OrdinalIgnoreCase);

    public string Path { get; }
    public uint Version { get; }
    public uint Stamp { get; }
    public string GameId { get; }
    public IReadOnlyList<RwdEntry> Entries { get; }

    RwdArchive(string path, FileStream stream, uint version, uint stamp, string gameId, long filesOffset, List<RwdEntry> entries)
    {
        Path = path;
        this.stream = stream;
        Version = version;
        Stamp = stamp;
        GameId = gameId;
        this.filesOffset = filesOffset;
        Entries = entries;
        foreach (var e in entries) byName[e.Name] = e;
    }

    public static RwdArchive Open(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var length = stream.Length;
            var reader = new BinaryReader(stream);
            if (length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "TGCK")
                throw new InvalidDataException($"{path} is not a Kohan II .rwd archive.");
            var version = reader.ReadUInt32();
            var count = reader.ReadUInt32();
            if (count < 3 || count > MaxSections) throw new InvalidDataException($"{path}: unexpected section count {count}.");
            var tableOffset = length - SectionRecord * (long)count;
            if (tableOffset < 12) throw new InvalidDataException($"{path}: the section table does not fit in the file.");
            stream.Seek(tableOffset, SeekOrigin.Begin);
            var sections = new Dictionary<string, (long Offset, long Size)>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var name = Encoding.Unicode.GetString(reader.ReadBytes(64)).Split('\0')[0];
                var offset = reader.ReadInt64();
                var size = reader.ReadInt64();
                reader.ReadBytes(16);
                if (offset < 12 || size < 0 || offset > tableOffset || size > tableOffset - offset)
                    throw new InvalidDataException($"{path}: section \"{name}\" lies outside the archive.");
                sections[name] = (offset, size);
            }
            if (!sections.TryGetValue("Header", out var header) || !sections.TryGetValue("Files", out var files) ||
                !sections.TryGetValue("Footer", out var footer))
                throw new InvalidDataException($"{path}: the Header, Files or Footer section is missing.");
            if (header.Size < 4 || header.Size > 4096) throw new InvalidDataException($"{path}: unexpected header size {header.Size}.");
            if (footer.Size < 4 || footer.Size > int.MaxValue) throw new InvalidDataException($"{path}: unexpected footer size {footer.Size}.");

            stream.Seek(header.Offset, SeekOrigin.Begin);
            var headerBytes = reader.ReadBytes((int)header.Size);
            var stamp = BitConverter.ToUInt32(headerBytes, headerBytes.Length - 4);
            var gameId = ReadGameId(headerBytes);

            stream.Seek(footer.Offset, SeekOrigin.Begin);
            var foot = new BinaryReader(new MemoryStream(reader.ReadBytes((int)footer.Size)));
            var entryCount = foot.ReadUInt32();
            if (entryCount > MaxEntries) throw new InvalidDataException($"{path}: {entryCount} entries is more than {MaxEntries}.");
            var entries = new List<RwdEntry>((int)entryCount);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < entryCount; i++)
            {
                if (foot.BaseStream.Length - foot.BaseStream.Position < 2) throw new InvalidDataException($"{path}: the footer ends inside entry {i}.");
                int chars = foot.ReadUInt16();
                if (chars == 0 || chars > MaxNameLength) throw new InvalidDataException($"{path}: entry {i} has a name of {chars} characters.");
                if (foot.BaseStream.Length - foot.BaseStream.Position < chars * 2L + 24) throw new InvalidDataException($"{path}: the footer ends inside entry {i}.");
                var raw = Encoding.Unicode.GetString(foot.ReadBytes(chars * 2));
                var offset = foot.ReadInt64();
                var size = foot.ReadInt64();
                var unknown = foot.ReadUInt32();
                var loadIndex = foot.ReadInt32();
                var name = NormalizeName(raw);
                // 0 for every entry of the retail archives; any other value has an unknown meaning (possibly
                // compression), so reading on could return wrong bytes without an error
                if (unknown != 0) throw new InvalidDataException($"{path}: \"{name}\" has the unsupported entry field value {unknown:x8}.");
                if (offset < 0 || size < 0 || offset > files.Size || size > files.Size - offset)
                    throw new InvalidDataException($"{path}: \"{name}\" lies outside the Files section.");
                if (!seen.Add(name)) throw new InvalidDataException($"{path}: \"{name}\" appears twice (names are not case-sensitive).");
                entries.Add(new RwdEntry(name, offset, size, loadIndex));
            }
            return new RwdArchive(path, stream, version, stamp, gameId, files.Offset, entries);
        }
        catch (EndOfStreamException error)
        {
            stream.Dispose();
            throw new InvalidDataException($"{path} is truncated.", error);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    static string ReadGameId(byte[] header)
    {
        // u32 2, u16 n, UTF-16 game id; anything else is reported as unknown rather than rejected
        if (header.Length < 6) return "";
        int chars = BitConverter.ToUInt16(header, 4);
        return 6 + chars * 2 <= header.Length - 4 ? Encoding.Unicode.GetString(header, 6, chars * 2) : "";
    }

    public RwdEntry? Find(string name) => byName.TryGetValue(NormalizeName(name), out var e) ? e : null;

    public byte[] Read(RwdEntry entry)
    {
        var data = new byte[entry.Size];
        stream.Seek(filesOffset + entry.Offset, SeekOrigin.Begin);
        stream.ReadExactly(data);
        return data;
    }

    public void Dispose() => stream.Dispose();

    /// <summary>
    /// A relative archive name with '/' separators. Rejects empty names and segments, ".." and ".", rooted paths,
    /// drive letters, control characters and characters Windows does not allow in file names.
    /// </summary>
    public static string NormalizeName(string name)
    {
        var n = name.Replace('\\', '/');
        if (n.Length == 0 || n.Length > MaxNameLength) throw new InvalidDataException($"Archive name of {n.Length} characters is not allowed.");
        if (n.StartsWith('/') || n.Contains(':')) throw new InvalidDataException($"Rooted archive name is not allowed: {name}");
        foreach (var segment in n.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                throw new InvalidDataException($"Archive name with an empty, \".\" or \"..\" segment is not allowed: {name}");
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new InvalidDataException($"Archive name segment ending in a space or dot is not allowed: {name}");
            foreach (var c in segment)
                if (c < 0x20 || "<>\"|?*".Contains(c)) throw new InvalidDataException($"Archive name with character U+{(int)c:X4} is not allowed: {name}");
        }
        return n;
    }

    /// <summary>Files in the order this writer stores them: names lower-cased and compared ordinally.</summary>
    public static List<KeyValuePair<string, byte[]>> SortForWrite(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        var list = files.Select(f => new KeyValuePair<string, byte[]>(NormalizeName(f.Key), f.Value)).ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in list)
            if (!seen.Add(f.Key)) throw new InvalidDataException($"\"{f.Key}\" appears twice (names are not case-sensitive).");
        return list.OrderBy(f => f.Key.ToLowerInvariant(), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A stamp that depends only on the content: CRC-32 of SHA-256 over (lower-cased name, NUL, bytes) per file in
    /// write order. The game's multiplayer join check compares each archive's name and header stamp, so equal
    /// content packed on two PCs gives equal stamps.
    /// </summary>
    public static uint ContentStamp(IReadOnlyList<KeyValuePair<string, byte[]>> sorted)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var f in sorted)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(f.Key.ToLowerInvariant() + "\0"));
            hash.AppendData(f.Value);
        }
        return Crc32(hash.GetHashAndReset());
    }

    /// <summary>
    /// Writes the files to an archive (through a temporary file, replaced at the end) and returns its stamp: the
    /// given one, or ContentStamp. Identical files give a byte-identical archive whatever their input order.
    /// </summary>
    public static uint Write(string path, IEnumerable<KeyValuePair<string, byte[]>> files, uint? stamp = null)
    {
        var sorted = SortForWrite(files);
        var value = stamp ?? ContentStamp(sorted);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(output))
        {
            writer.Write("TGCK"u8);
            writer.Write(2u);
            writer.Write(3u);
            var headerOffset = output.Position;
            writer.Write(2u);
            writer.Write((ushort)2);
            writer.Write(Encoding.Unicode.GetBytes("K2"));
            writer.Write(0u);
            writer.Write(value);
            var headerSize = output.Position - headerOffset;
            var filesStart = output.Position;
            var offsets = new long[sorted.Count];
            long position = 0;
            for (var i = 0; i < sorted.Count; i++)
            {
                offsets[i] = position;
                writer.Write(sorted[i].Value);
                position += sorted[i].Value.Length;
            }
            var footerOffset = output.Position;
            writer.Write(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                writer.Write((ushort)sorted[i].Key.Length);
                writer.Write(Encoding.Unicode.GetBytes(sorted[i].Key));
                writer.Write(offsets[i]);
                writer.Write((long)sorted[i].Value.Length);
                writer.Write(0u);
                writer.Write(i);
            }
            var footerSize = output.Position - footerOffset;
            foreach (var (name, offset, size) in new[] { ("Header", headerOffset, headerSize), ("Files", filesStart, position), ("Footer", footerOffset, footerSize) })
            {
                var record = new byte[64];
                Encoding.Unicode.GetBytes(name).CopyTo(record, 0);
                writer.Write(record);
                writer.Write(offset);
                writer.Write(size);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(size);
            }
        }
        File.Move(temporary, path, overwrite: true);
        return value;
    }

    static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
