using System.Diagnostics;
using System.Security.Cryptography;
using PawsPatchLauncher.Rwd;

internal static class RwdRelocatedPathsTests
{
    internal static int Run(string root)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var passed = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("RWD relocated paths: " + message); passed++; }
        var fixture = Path.Combine(root, "rwd-relocated"); Directory.CreateDirectory(fixture);
        var real = Path.Combine(fixture, "physical library"); Directory.CreateDirectory(real);
        var alias = Path.Combine(fixture, "Steam library");
        var input = Path.Combine(real, "input"); Directory.CreateDirectory(input);
        File.WriteAllText(Path.Combine(input, "hello.txt"), "hello");
        var original = Path.Combine(real, "original.rwd");
        var files = new Dictionary<string, byte[]> { ["hello.txt"] = "hello"u8.ToArray() };
        RwdArchive.Write(original, files);
        using (Junction(alias, real))
        {
            using (var archive = RwdArchive.Open(Path.Combine(alias, "original.rwd")))
                Check(archive.Read(archive.Entries.Single()).SequenceEqual("hello"u8.ToArray()), "archive read through a relocated parent");
            var packed = Path.Combine(alias, "nested", "packed.rwd");
            Check(RwdOperations.Pack(Path.Combine(alias, "input"), packed).Files == 1, "pack through a relocated parent");
            Check(Sha(packed) == Sha(original), "relocation changes archive bytes");
            Check(RwdOperations.Extract(packed, Path.Combine(alias, "new", "output")) == (1, 0), "extract creates destination through a relocated parent");
            Check(File.ReadAllText(Path.Combine(real, "new", "output", "hello.txt")) == "hello", "extract writes the selected physical destination");
            Check(RwdOperations.Extract(packed, alias) == (1, 0), "selected extraction root can itself be a junction");
            RwdArchive.Write(Path.Combine(alias, "written.rwd"), files);
            Check(Sha(Path.Combine(real, "written.rwd")) == Sha(original), "writer accepts the selected linked parent");
            RwdArchive.WriteFiles(Path.Combine(alias, "from-files.rwd"), new Dictionary<string, string> { ["hello.txt"] = Path.Combine(alias, "input", "hello.txt") });
            Check(Sha(Path.Combine(real, "from-files.rwd")) == Sha(original), "explicit source file through a junction");
            var inputAlias = Path.Combine(fixture, "chosen input");
            using (Junction(inputAlias, input))
            {
                var self = Path.Combine(input, "self.rwd");
                RwdArchive.Write(self, files);
                Check(RwdOperations.Pack(inputAlias, self).Files == 1, "exclude the output archive when input uses an alias");
                File.Delete(self);
            }
            var secondAlias = Path.Combine(fixture, "second alias");
            using (Junction(secondAlias, alias))
                Check(RwdOperations.Extract(original, Path.Combine(secondAlias, "nested", "second")) == (1, 0), "chained junctions in the selected path");
            var selfArchive = Path.Combine(real, "self.rwd");
            RwdArchive.Write(selfArchive, new Dictionary<string, byte[]> { ["self.rwd"] = [1] });
            var before = Sha(selfArchive);
            Check(Reject(() => RwdOperations.Extract(Path.Combine(alias, "self.rwd"), real, true)), "self overwrite through an alias must be rejected");
            Check(Sha(selfArchive) == before, "self overwrite leaves the original intact");

            var outside = Path.Combine(fixture, "outside"); Directory.CreateDirectory(outside);
            var sentinel = Path.Combine(outside, "probe.txt"); File.WriteAllText(sentinel, "sentinel");
            var output = Path.Combine(real, "output"); Directory.CreateDirectory(output);
            var hostile = Path.Combine(real, "hostile.rwd");
            RwdArchive.Write(hostile, new Dictionary<string, byte[]> { ["escape/probe.txt"] = [1] });
            using (Junction(Path.Combine(output, "escape"), outside))
            {
                Check(Reject(() => RwdOperations.Extract(hostile, Path.Combine(alias, "output"), true)), "nested extraction link remains rejected");
                Check(Reject(() => RwdOperations.Pack(Path.Combine(alias, "output"), Path.Combine(real, "rejected.rwd"))), "nested pack link remains rejected");
                Check(File.ReadAllText(sentinel) == "sentinel", "nested link modified an outside file");
            }
            using (Junction(Path.Combine(output, "escape"), Path.Combine(fixture, "missing")))
                Check(Reject(() => RwdOperations.Extract(hostile, Path.Combine(alias, "output"), true)), "dangling nested junction remains rejected");

            // A changed alias must not redirect an operation that already chose
            // its root; its physical directory must remain pinned against rename.
            var pinnedAlias = Path.Combine(fixture, "pinned alias");
            IDisposable? selectedLink = Junction(pinnedAlias, real);
            try
            {
                using var chosen = RwdFileSystem.LockRoot(pinnedAlias);
                var renamed = Path.Combine(fixture, "renamed library");
                var renameBlocked = false;
                try { Directory.Move(real, renamed); }
                catch (IOException) { renameBlocked = true; }
                if (!renameBlocked) Directory.Move(renamed, real);
                Check(renameBlocked, "physical directory can be replaced while in use");
                using var opened = RwdArchive.Open(Path.Combine(pinnedAlias, "original.rwd"));
                selectedLink.Dispose(); selectedLink = null;
                selectedLink = Junction(pinnedAlias, outside);
                RwdFileSystem.AtomicWrite(Path.Combine(chosen.DirectoryPath, "pinned.txt"), true, stream => stream.Write("selected"u8));
                Check(File.ReadAllText(Path.Combine(real, "pinned.txt")) == "selected" && !File.Exists(Path.Combine(outside, "pinned.txt")), "alias replacement redirected a write");
                Check(opened.Read(opened.Entries.Single()).SequenceEqual("hello"u8.ToArray()), "alias replacement redirected an open archive read");
            }
            finally { selectedLink?.Dispose(); }

            // Deterministically replace a nested input directory after listing
            // its file but before WriteFiles reopens it to copy the payload.
            var nested = Path.Combine(real, "listed-input"); Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "probe.txt"), "inside");
            var savedNested = Path.Combine(real, "listed-input-original");
            IDisposable? swappedLink = null;
            try
            {
                IEnumerable<KeyValuePair<string, string>> Swapping()
                {
                    yield return new("probe.txt", Path.Combine(nested, "probe.txt"));
                    Directory.Move(nested, savedNested);
                    swappedLink = Junction(nested, outside);
                }
                var beforeWrite = Sha(original);
                Check(Reject(() => RwdArchive.WriteFiles(original, Swapping(), null, treeSources: true)), "nested input replacement followed a link during packing");
                Check(Sha(original) == beforeWrite && File.ReadAllText(sentinel) == "sentinel", "rejected input replacement changed existing files");
                Check(!Directory.GetFiles(real, "original.rwd.*.tmp").Any(), "failed pack left temporary files");
            }
            finally { swappedLink?.Dispose(); }
        }
        Console.WriteLine($"RWD RELOCATED PATHS PASS {passed}");
        return passed;
    }

    internal static IDisposable Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var message = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Junction fixture: " + message);
        return new JunctionCleanup(Path.GetFullPath(link));
    }
    sealed class JunctionCleanup(string path) : IDisposable
    {
        public void Dispose()
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) throw new IOException("Fixture junction changed: " + path);
            Directory.Delete(path); // Delete this exact link only, never recurse into its target.
        }
    }
    static bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
    static string Sha(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)); }
}
