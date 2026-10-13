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
        // Independently reproduced with Python struct + hashlib + zlib (stamp v2).
        Check(stamp == 0x66d98411u, $"The stamp rule changed: {stamp:x8}.");
        Check(Sha(a) == "0628D44B45EB9C01B3FF0FB317591977C741528303A18AD9A501654DCF6E5536", "The archive layout changed.");
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
        var name = IndexOf(past, Encoding.Unicode.GetBytes("avars_test.tgi"));
        BitConverter.GetBytes(1L << 40).CopyTo(past, name + Encoding.Unicode.GetByteCount("avars_test.tgi") + 8);
        Check(Throws(() => Open(folder, "past.rwd", past)), "An entry larger than the Files section was accepted.");
        var unknown = (byte[])good.Clone();
        BitConverter.GetBytes(1u).CopyTo(unknown, name + Encoding.Unicode.GetByteCount("avars_test.tgi") + 16);
        Check(Throws(() => Open(folder, "unknown.rwd", unknown)), "An entry with a non-zero unknown field was accepted.");
        Hardening(folder, Check);
        Console.WriteLine($"RWD ARCHIVE PASS {n}: round trip, deterministic golden output, stamp rule, unsafe names, hostile and damaged archives; no game files");
        return n;
    }

    static void Hardening(string root, Action<bool, string> check)
    {
        static KeyValuePair<string, byte[]> F(string name, string text) => new(name, Encoding.UTF8.GetBytes(text));
        var one = new[] { F("a.bin", "oneb.bin\0two") };
        var two = new[] { F("a.bin", "one"), F("b.bin", "two") };
        check(RwdArchive.ContentStamp(one) != RwdArchive.ContentStamp(two), "Ambiguous entry-boundary stamp collision remains.");
        var canonical = Path.Combine(root, "canonical.rwd");
        var cased = Path.Combine(root, "cased.rwd");
        RwdArchive.Write(canonical, new[] { F("Dir/File.TXT", "data") });
        RwdArchive.Write(cased, new[] { F("dir/file.txt", "data") });
        check(Sha(canonical) == Sha(cased), "Equivalent name casing changes archive bytes.");
        foreach (var bad in new[] { "CON", "nul.txt", "dir/AUX.bin", "LPT1", "COM9.log", "con .txt", "COM¹", "LPT².txt", "CONIN$", "x/CONOUT$", "bad\ud800.txt" })
            check(Throws(() => RwdArchive.NormalizeName(bad)), "Device or malformed Unicode name accepted: " + bad);
        check(RwdArchive.NormalizeName("company/com10.txt") == "company/com10.txt", "Ordinary non-device name rejected.");
        File.WriteAllText(canonical + ".tmp", "unrelated sentinel");
        RwdArchive.Write(canonical, two);
        check(File.ReadAllText(canonical + ".tmp") == "unrelated sentinel", "Writer overwrote someone else's temporary file.");
        check(!Directory.EnumerateFiles(root, "canonical.rwd.*.tmp").Any(), "Owned temporary file leaked.");
        using (var archive = RwdArchive.Open(canonical))
        {
            check(Throws(() => archive.Read(new RwdEntry("a.bin", -100, 3, 0))), "Forged entry offset accepted.");
            using var copy = new MemoryStream(); archive.CopyTo(archive.Find("a.bin")!, copy);
            check(Encoding.UTF8.GetString(copy.ToArray()) == "one", "Streaming read differs.");
        }
        var bytes = File.ReadAllBytes(canonical);
        var table = bytes.Length - 3 * 96;
        void Bad(string label, Action<byte[]> mutate)
        {
            var data = (byte[])bytes.Clone(); mutate(data);
            check(Throws(() => Open(root, label + ".rwd", data)), "Malformed archive accepted: " + label);
        }
        Bad("version", data => BitConverter.GetBytes(3u).CopyTo(data, 4));
        Bad("duplicate-section", data => Array.Copy(data, table, data, table + 96, 64));
        Bad("section-overlap", data => BitConverter.GetBytes(12L).CopyTo(data, table + 96 + 64));
        Bad("section-size-copy", data => BitConverter.GetBytes(99L).CopyTo(data, table + 88));
        var footOffset = (int)BitConverter.ToInt64(bytes, table + 192 + 64);
        Bad("entry-count", data => BitConverter.GetBytes(uint.MaxValue).CopyTo(data, footOffset));
        Bad("invalid-unicode", data => BitConverter.GetBytes((ushort)0xd800).CopyTo(data, footOffset + 6));
        var empty = Path.Combine(root, "empty.rwd"); RwdArchive.Write(empty, Array.Empty<KeyValuePair<string, byte[]>>());
        using (var archive = RwdArchive.Open(empty)) check(archive.Entries.Count == 0, "Empty archive failed.");

        var input = Path.Combine(root, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "hello.txt"), "hello");
        var packed = Path.Combine(root, "packed.rwd"); var result = RwdOperations.Pack(input, packed);
        var output = Path.Combine(root, "extract");
        check(result.Files == 1 && RwdOperations.Extract(packed, output) == (1, 0), "Pack/extract round trip failed.");
        File.WriteAllText(Path.Combine(output, "hello.txt"), "keep");
        check(RwdOperations.Extract(packed, output) == (0, 1) && File.ReadAllText(Path.Combine(output, "hello.txt")) == "keep", "Default extract replaced a file.");
        check(RwdOperations.Extract(packed, output, true) == (1, 0) && File.ReadAllText(Path.Combine(output, "hello.txt")) == "hello", "Explicit overwrite failed.");
        var collision = Path.Combine(root, "collision.rwd");
        RwdArchive.Write(collision, new[] { F("parent", "file"), F("parent/child", "file") });
        var collisionOut = Path.Combine(root, "collision-output");
        check(Throws(() => RwdOperations.Extract(collision, collisionOut)), "File/directory collision accepted.");
        check(!Directory.EnumerateFileSystemEntries(collisionOut).Any(), "Collision wrote a partial extraction.");

        var before = Sha(packed); var missing = Path.Combine(input, "vanishing.txt"); File.WriteAllText(missing, "temporary");
        IEnumerable<KeyValuePair<string, string>> Vanishing()
        {
            yield return new("vanishing.txt", missing);
            File.Delete(missing);
        }
        bool failed = false;
        try { RwdArchive.WriteFiles(packed, Vanishing()); } catch (IOException) { failed = true; }
        check(failed && Sha(packed) == before, "Failed pack destroyed the previous archive.");
        check(!Directory.EnumerateFiles(root, "packed.rwd.*.tmp").Any(), "Failed pack leaked a temporary file.");

        if (OperatingSystem.IsWindows())
        {
            var outside = Path.Combine(root, "outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "probe.txt"), "sentinel");
            var link = Path.Combine(output, "linked"); Junction(link, outside);
            var linked = Path.Combine(root, "linked.rwd"); RwdArchive.Write(linked, new[] { F("linked/probe.txt", "overwrite") });
            check(Throws(() => RwdOperations.Extract(linked, output, true)), "Extraction followed a directory junction.");
            check(File.ReadAllText(Path.Combine(outside, "probe.txt")) == "sentinel", "Extraction modified a junction target.");
            check(Throws(() => RwdOperations.Extract(packed, link, true)), "Extraction root junction accepted.");
            check(Throws(() => RwdOperations.Pack(output, Path.Combine(root, "linked-pack.rwd"))), "Packing followed an input junction.");
            check(Throws(() => RwdArchive.Write(Path.Combine(link, "escape.rwd"), one)), "Writer followed an output parent junction.");
            Directory.Delete(link); // Only the fixture junction itself, never its target.
            check(File.ReadAllText(Path.Combine(outside, "probe.txt")) == "sentinel", "Junction cleanup affected its target.");
            var hardLink = Path.Combine(output, "hard.txt");
            if (!CreateHardLink(hardLink, Path.Combine(outside, "probe.txt"), IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error());
            RwdArchive.Write(linked, new[] { F("hard.txt", "new contents") });
            check(RwdOperations.Extract(linked, output, true) == (1, 0), "Hard-linked destination could not be replaced safely.");
            check(File.ReadAllText(hardLink) == "new contents" && File.ReadAllText(Path.Combine(outside, "probe.txt")) == "sentinel", "Overwrite wrote through a hard link.");
        }

        // A payload larger than the buffered API limit must stream through pack,
        // verify and extract with allocation independent of payload size.
        var largeInput = Path.Combine(root, "large-input"); Directory.CreateDirectory(largeInput);
        using (var large = File.Create(Path.Combine(largeInput, "large.bin"))) large.SetLength(RwdArchive.MaxBufferedEntry + 1L);
        var largeArchive = Path.Combine(root, "large.rwd");
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        RwdOperations.Pack(largeInput, largeArchive);
        using (var archive = RwdArchive.Open(largeArchive))
        {
            var entry = archive.Entries.Single();
            check(Throws(() => archive.Read(entry)), "Buffered API allocated an oversized entry.");
            archive.CopyTo(entry, Stream.Null);
        }
        var largeOut = Path.Combine(root, "large-output"); RwdOperations.Extract(largeArchive, largeOut);
        check(GC.GetAllocatedBytesForCurrentThread() - allocated < 16 * 1024 * 1024, "Streaming allocated payload-sized arrays.");
        check(new FileInfo(Path.Combine(largeOut, "large.bin")).Length == RwdArchive.MaxBufferedEntry + 1L, "Large extraction truncated.");
    }

    static void Junction(string link, string target)
    {
        var start = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = System.Diagnostics.Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new Exception("Junction fixture failed: " + output);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    static extern bool CreateHardLink(string newName, string existingName, IntPtr security);

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
