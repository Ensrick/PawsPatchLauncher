using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

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
    public const int MaxEntries = 100_000;
    public const int MaxNameLength = 1024;
    public const int MaxBufferedEntry = 64 * 1024 * 1024;
    const int MaxMetadataBytes = 64 * 1024 * 1024;
    public const long MaxPayloadBytes = 16L * 1024 * 1024 * 1024;
    static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);
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
        Entries = entries.AsReadOnly();
        foreach (var e in entries) byName[e.Name] = e;
    }

    public static RwdArchive Open(string path)
    {
        var stream = RwdFileSystem.OpenRead(path);
        try
        {
            var length = stream.Length;
            var reader = new BinaryReader(stream);
            if (length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "TGCK")
                throw new InvalidDataException($"{path} is not a Kohan II .rwd archive.");
            var version = reader.ReadUInt32();
            if (version != 2) throw new InvalidDataException("Unsupported TGCK version.");
            var count = reader.ReadUInt32();
            if (count < 3 || count > MaxSections) throw new InvalidDataException($"{path}: unexpected section count {count}.");
            var tableOffset = length - SectionRecord * (long)count;
            if (tableOffset < 12) throw new InvalidDataException($"{path}: the section table does not fit in the file.");
            stream.Seek(tableOffset, SeekOrigin.Begin);
            var sections = new Dictionary<string, (long Offset, long Size)>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var rawName = Utf16.GetString(reader.ReadBytes(64));
                var nul = rawName.IndexOf('\0');
                if (nul <= 0 || rawName.AsSpan(nul).IndexOfAnyExcept('\0') >= 0)
                    throw new InvalidDataException("Invalid section name.");
                var name = rawName[..nul];
                var offset = reader.ReadInt64();
                var size = reader.ReadInt64();
                reader.ReadUInt64(); // Retail writers left non-semantic filler here.
                var repeatedSize = reader.ReadInt64();
                if (offset < 12 || size < 0 || offset > tableOffset || size > tableOffset - offset)
                    throw new InvalidDataException($"{path}: section \"{name}\" lies outside the archive.");
                if (repeatedSize != size || !sections.TryAdd(name, (offset, size)))
                    throw new InvalidDataException("Invalid or duplicate section.");
            }
            long sectionEnd = 12;
            foreach (var section in sections.Values.Where(s => s.Size > 0).OrderBy(s => s.Offset))
            {
                if (section.Offset < sectionEnd) throw new InvalidDataException("Overlapping sections.");
                sectionEnd = section.Offset + section.Size;
            }
            if (!sections.TryGetValue("Header", out var header) || !sections.TryGetValue("Files", out var files) ||
                !sections.TryGetValue("Footer", out var footer))
                throw new InvalidDataException($"{path}: the Header, Files or Footer section is missing.");
            if (header.Size != 18) throw new InvalidDataException($"{path}: unexpected header size {header.Size}.");
            if (footer.Size < 4 || footer.Size > MaxMetadataBytes || files.Size > MaxPayloadBytes)
                throw new InvalidDataException("Archive exceeds resource limits.");

            stream.Seek(header.Offset, SeekOrigin.Begin);
            var headerBytes = reader.ReadBytes((int)header.Size);
            var stamp = BitConverter.ToUInt32(headerBytes, headerBytes.Length - 4);
            var gameId = ReadGameId(headerBytes);
            if (gameId != "K2" || BitConverter.ToUInt32(headerBytes, 0) != 2 || BitConverter.ToUInt32(headerBytes, 10) != 0)
                throw new InvalidDataException("Unsupported game header.");

            stream.Seek(footer.Offset, SeekOrigin.Begin);
            // Parse metadata directly from the bounded section; no whole-footer allocation.
            var foot = reader;
            var footerEnd = footer.Offset + footer.Size;
            var entryCount = foot.ReadUInt32();
            if (entryCount > MaxEntries || entryCount > (footer.Size - 4) / 28)
                throw new InvalidDataException("Excessive entry count.");
            var entries = new List<RwdEntry>((int)entryCount);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expandedBytes = 0;
            for (var i = 0; i < entryCount; i++)
            {
                if (footerEnd - stream.Position < 2) throw new InvalidDataException($"{path}: the footer ends inside entry {i}.");
                int chars = foot.ReadUInt16();
                if (chars == 0 || chars > MaxNameLength) throw new InvalidDataException($"{path}: entry {i} has a name of {chars} characters.");
                if (footerEnd - stream.Position < chars * 2L + 24) throw new InvalidDataException($"{path}: the footer ends inside entry {i}.");
                var raw = Utf16.GetString(foot.ReadBytes(chars * 2));
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
                if (size > MaxPayloadBytes - expandedBytes)
                    throw new InvalidDataException("Expanded archive payload exceeds the limit.");
                expandedBytes += size;
                if (!seen.Add(name)) throw new InvalidDataException($"{path}: \"{name}\" appears twice (names are not case-sensitive).");
                entries.Add(new RwdEntry(name, offset, size, loadIndex));
            }
            if (stream.Position != footerEnd) throw new InvalidDataException("Unexpected footer trailing bytes.");
            return new RwdArchive(path, stream, version, stamp, gameId, files.Offset, entries);
        }
        catch (EndOfStreamException error)
        {
            stream.Dispose();
            throw new InvalidDataException($"{path} is truncated.", error);
        }
        catch (DecoderFallbackException error)
        {
            stream.Dispose();
            throw new InvalidDataException("Invalid UTF-16 archive name.", error);
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
        ValidateEntry(entry);
        if (entry.Size > MaxBufferedEntry) throw new InvalidDataException("Use streaming CopyTo for large entries.");
        var data = new byte[entry.Size];
        stream.Seek(filesOffset + entry.Offset, SeekOrigin.Begin);
        stream.ReadExactly(data);
        return data;
    }

    void ValidateEntry(RwdEntry entry)
    {
        if (!byName.TryGetValue(entry.Name, out var own) || own != entry)
            throw new InvalidDataException("Entry does not belong to this archive.");
    }

    public void CopyTo(RwdEntry entry, Stream output)
    {
        ValidateEntry(entry);
        stream.Position = filesOffset + entry.Offset;
        Transfer(stream, output, entry.Size, null);
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
        try { _ = Utf16.GetByteCount(n); }
        catch (EncoderFallbackException error) { throw new InvalidDataException("Invalid Unicode name.", error); }
        foreach (var segment in n.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
                throw new InvalidDataException($"Archive name with an empty, \".\" or \"..\" segment is not allowed: {name}");
            if (segment.EndsWith(' ') || segment.EndsWith('.'))
                throw new InvalidDataException($"Archive name segment ending in a space or dot is not allowed: {name}");
            foreach (var c in segment)
                if (c < 0x20 || "<>\"|?*".Contains(c)) throw new InvalidDataException($"Archive name with character U+{(int)c:X4} is not allowed: {name}");
            var stem = segment.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            if (stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
                 "123456789¹²³".Contains(stem[3])))
                throw new InvalidDataException("Windows device names are not allowed.");
        }
        return n;
    }

    /// <summary>Files in the order this writer stores them: names lower-cased and compared ordinally.</summary>
    public static List<KeyValuePair<string, byte[]>> SortForWrite(IEnumerable<KeyValuePair<string, byte[]>> files)
    {
        var list = new List<KeyValuePair<string, byte[]>>();
        long total = 0;
        foreach (var f in files)
        {
            if (list.Count == MaxEntries || f.Value == null || f.Value.LongLength > MaxPayloadBytes - total)
                throw new InvalidDataException("Input exceeds resource limits.");
            total += f.Value.LongLength;
            list.Add(new(NormalizeName(f.Key).ToLowerInvariant(), f.Value));
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in list)
            if (!seen.Add(f.Key)) throw new InvalidDataException($"\"{f.Key}\" appears twice (names are not case-sensitive).");
        return list.OrderBy(f => f.Key.ToLowerInvariant(), StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Length-framed stamp v2. The stored stamp remains 32 bits and is not a
    /// cryptographic proof of equality; retain full hashes for stronger comparisons.
    /// </summary>
    public static uint ContentStamp(IReadOnlyList<KeyValuePair<string, byte[]>> sorted)
    {
        var files = SortForWrite(sorted);
        using var hash = StartHash(files.Count);
        foreach (var f in files)
        {
            HashEntry(hash, f.Key, f.Value.LongLength);
            hash.AppendData(f.Value);
        }
        return Crc32(hash.GetHashAndReset());
    }

    static IncrementalHash StartHash(int count)
    {
        var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.ASCII.GetBytes("PAW-RWD-STAMP-2\0"));
        Span<byte> n = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(n, count); hash.AppendData(n);
        return hash;
    }

    static void HashEntry(IncrementalHash hash, string name, long length)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        Span<byte> n = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(n, bytes.Length); hash.AppendData(n[..4]); hash.AppendData(bytes);
        BinaryPrimitives.WriteInt64LittleEndian(n, length); hash.AppendData(n);
    }

    sealed record Source(string Name, long Length, Func<Stream> Open);

    public static uint WriteFiles(string path, IEnumerable<KeyValuePair<string, string>> files, uint? stamp = null)
    {
        var sources = new List<Source>(); var seen = new HashSet<string>(StringComparer.Ordinal); long total = 0;
        foreach (var f in files)
        {
            var name = NormalizeName(f.Key).ToLowerInvariant(); var full = System.IO.Path.GetFullPath(f.Value);
            using var input = RwdFileSystem.OpenRead(full);
            if (sources.Count == MaxEntries || !seen.Add(name) || input.Length > MaxPayloadBytes - total)
                throw new InvalidDataException("Duplicate or excessive input files.");
            total += input.Length; sources.Add(new(name, input.Length, () => RwdFileSystem.OpenRead(full)));
        }
        return WriteSources(path, sources.OrderBy(s => s.Name, StringComparer.Ordinal).ToList(), stamp);
    }

    /// <summary>
    /// Writes the files to an archive (through a temporary file, replaced at the end) and returns its stamp: the
    /// given one, or ContentStamp. Identical files give a byte-identical archive whatever their input order.
    /// </summary>
    public static uint Write(string path, IEnumerable<KeyValuePair<string, byte[]>> files, uint? stamp = null)
    {
        var sorted = SortForWrite(files);
        return WriteSources(path, sorted.Select(f => new Source(f.Key, f.Value.LongLength, () => new MemoryStream(f.Value, false))).ToList(), stamp);
    }

    static uint WriteSources(string path, List<Source> sorted, uint? stamp)
    {
        if (4L + sorted.Sum(f => 26L + Encoding.Unicode.GetByteCount(f.Name)) > MaxMetadataBytes)
            throw new InvalidDataException("Archive metadata exceeds the limit.");
        uint value = 0;
        RwdFileSystem.AtomicWrite(path, true, output =>
        {
            using var writer = new BinaryWriter(output, Encoding.UTF8, true);
            writer.Write("TGCK"u8);
            writer.Write(2u);
            writer.Write(3u);
            var headerOffset = output.Position;
            writer.Write(2u);
            writer.Write((ushort)2);
            writer.Write(Encoding.Unicode.GetBytes("K2"));
            writer.Write(0u);
            var stampOffset = output.Position;
            writer.Write(0u);
            var headerSize = output.Position - headerOffset;
            var filesStart = output.Position;
            var offsets = new long[sorted.Count];
            long position = 0;
            using var hash = StartHash(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                var f = sorted[i];
                offsets[i] = position;
                HashEntry(hash, f.Name, f.Length);
                using var input = f.Open();
                if (input.Length != f.Length) throw new IOException("Input changed during packing.");
                Transfer(input, output, f.Length, hash);
                if (input.ReadByte() != -1) throw new IOException("Input grew during packing.");
                position += f.Length;
            }
            value = stamp ?? Crc32(hash.GetHashAndReset());
            var footerOffset = output.Position;
            writer.Write(sorted.Count);
            for (var i = 0; i < sorted.Count; i++)
            {
                writer.Write((ushort)sorted[i].Name.Length);
                writer.Write(Encoding.Unicode.GetBytes(sorted[i].Name));
                writer.Write(offsets[i]);
                writer.Write(sorted[i].Length);
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
            output.Position = stampOffset; writer.Write(value); writer.Flush();
        });
        return value;
    }

    static void Transfer(Stream input, Stream output, long size, IncrementalHash? hash)
    {
        var buffer = new byte[64 * 1024];
        while (size > 0)
        {
            int n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, size));
            if (n == 0) throw new EndOfStreamException("Truncated payload.");
            output.Write(buffer, 0, n); hash?.AppendData(buffer, 0, n); size -= n;
        }
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
