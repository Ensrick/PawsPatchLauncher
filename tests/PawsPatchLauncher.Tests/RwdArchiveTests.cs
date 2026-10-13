using System.Security.Cryptography;
using System.Text;
using PawsPatchLauncher.Rwd;

// RwdTool's archive reader and writer on small self-authored files: round trip, determinism, name rules and
// damaged archives. No game installation is used.
internal static class RwdArchiveTests
{
    internal static int Run(string root)
    {
        var folder = Path.Combine(root, "rwd-archive"); Directory.CreateDirectory(folder);
        var n = 0; void Check(bool ok, string why) { if (!ok) throw new Exception("RWD archive: " + why); n++; }
        static KeyValuePair<string, byte[]> F(string name, string text) => new(name, Encoding.UTF8.GetBytes(text));
        var files = new[]
        {
            F("data/Units/Test/test_unit.tgi", "unit test data\r\n"),
            F("data/Game/SVars_test.tgi", "string Version = \"1.0\"\r\n"),
            F("AVars_test.tgi", "a"),
            F("empty.txt", ""),
            F("Données/é ü.txt", "unicode name"),
        };

        // round trip: every name, size and byte back as written; the stamp is derived from the content
        var a = Path.Combine(folder, "a.rwd");
        var stamp = RwdArchive.Write(a, files);
        using (var archive = RwdArchive.Open(a))
        {
            Check(archive.Stamp == stamp && archive.Version == 2 && archive.GameId == "K2", "Header did not round-trip.");
            Check(archive.Entries.Count == files.Length, "Entry count changed.");
            foreach (var f in files)
            {
                var e = archive.Find(f.Key);
                Check(e != null && archive.Read(e).AsSpan().SequenceEqual(f.Value), $"\"{f.Key}\" did not round-trip.");
            }
            Check(archive.Find("DATA\\units\\TEST\\Test_Unit.TGI") != null, "Lookup is not case-insensitive or does not accept '\\'.");
            Check(archive.Entries.Select(e => e.LoadIndex).SequenceEqual(Enumerable.Range(0, files.Length)), "Load indices are not 0..n-1.");
            var order = archive.Entries.OrderBy(e => e.LoadIndex).Select(e => e.Name.ToLowerInvariant()).ToList();
            Check(order.SequenceEqual(order.OrderBy(x => x, StringComparer.Ordinal)), "Files are not stored in lower-cased ordinal order.");
            Check(archive.Find("empty.txt")!.Size == 0, "An empty file did not keep size 0.");
        }

        // determinism: the same files in another order give the same bytes; one changed byte changes the stamp
        var b = Path.Combine(folder, "b.rwd");
        Check(RwdArchive.Write(b, files.Reverse()) == stamp, "Input order changed the stamp.");
        Check(Sha(a) == Sha(b), "Input order changed the archive bytes.");
        // golden values: peers on different launcher builds must pack the same files to the same stamp, so any change
        // to the layout or the stamp rule has to be deliberate (values from `RwdTool pack` on these five files)
        Check(stamp == 0x6269b5c7u, $"The stamp rule changed: {stamp:x8}.");
        Check(Sha(a) == "36947450CCA953FD6D5F59A2B315C6C6BBFF58316E51A093B15FF287FAE81AF2", "The archive layout changed.");
        var changed = files.Select(f => f.Key == "AVars_test.tgi" ? F(f.Key, "b") : f).ToArray();
        Check(RwdArchive.Write(Path.Combine(folder, "c.rwd"), changed) != stamp, "A changed file kept the same stamp.");
        Check(RwdArchive.Write(Path.Combine(folder, "d.rwd"), files, 0x12345678u) == 0x12345678u, "An explicit stamp was not used.");
        using (var d = RwdArchive.Open(Path.Combine(folder, "d.rwd"))) Check(d.Stamp == 0x12345678u, "An explicit stamp was not stored.");

        // names: duplicates that differ only in case, traversal, rooted paths and Windows-invalid characters
        Check(Throws(() => RwdArchive.Write(Path.Combine(folder, "dup.rwd"), new[] { F("x/A.tgi", "1"), F("X/a.TGI", "2") })), "Case-insensitive duplicate accepted.");
        foreach (var bad in new[] { "../evil.tgi", "a/../../b.tgi", "/rooted.tgi", "C:/drive.tgi", "a//b.tgi", "a/./b.tgi", "trailing./x", "q?.tgi", "tab\t.tgi", "" })
            Check(Throws(() => RwdArchive.NormalizeName(bad)), $"Unsafe name accepted: \"{bad}\".");
        Check(RwdArchive.NormalizeName("a\\b\\c.tgi") == "a/b/c.tgi", "Backslashes were not normalized.");

        // a hostile archive: a valid file whose stored name is rewritten to escape the target folder
        var hostile = Path.Combine(folder, "hostile.rwd");
        RwdArchive.Write(hostile, new[] { F("aa/b.tgi", "x") });
        var bytes = File.ReadAllBytes(hostile);
        var at = IndexOf(bytes, Encoding.Unicode.GetBytes("aa/b.tgi"));
        Encoding.Unicode.GetBytes("../b.tgi").CopyTo(bytes, at);
        File.WriteAllBytes(hostile, bytes);
        Check(Throws(() => RwdArchive.Open(hostile).Dispose()), "An archive with a \"..\" name was opened.");

        // damaged archives: truncated, wrong magic, an entry pointing past the Files section, huge section count
        var good = File.ReadAllBytes(a);
        Check(Throws(() => Open(folder, "truncated.rwd", good[..(good.Length - 50)])), "A truncated archive was opened.");
        var magic = (byte[])good.Clone(); magic[0] = (byte)'X';
        Check(Throws(() => Open(folder, "magic.rwd", magic)), "An archive with a wrong magic was opened.");
        var sections = (byte[])good.Clone(); BitConverter.GetBytes(9999u).CopyTo(sections, 8);
        Check(Throws(() => Open(folder, "sections.rwd", sections)), "An absurd section count was accepted.");
        var past = (byte[])good.Clone();
        var name = IndexOf(past, Encoding.Unicode.GetBytes("AVars_test.tgi"));
        BitConverter.GetBytes(1L << 40).CopyTo(past, name + Encoding.Unicode.GetByteCount("AVars_test.tgi") + 8);
        Check(Throws(() => Open(folder, "past.rwd", past)), "An entry larger than the Files section was accepted.");
        var unknown = (byte[])good.Clone();
        BitConverter.GetBytes(1u).CopyTo(unknown, name + Encoding.Unicode.GetByteCount("AVars_test.tgi") + 16);
        Check(Throws(() => Open(folder, "unknown.rwd", unknown)), "An entry with a non-zero unknown field was accepted.");
        Console.WriteLine($"RWD ARCHIVE PASS {n}: round trip, deterministic golden output, stamp rule, unsafe names, hostile and damaged archives; no game files");
        return n;
    }

    static string Sha(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    static void Open(string folder, string file, byte[] data)
    {
        var path = Path.Combine(folder, file);
        File.WriteAllBytes(path, data);
        RwdArchive.Open(path).Dispose();
    }

    static bool Throws(Action action)
    {
        try { action(); return false; }
        catch (InvalidDataException) { return true; }
    }

    static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new Exception("RWD archive test: marker not found");
    }
}
