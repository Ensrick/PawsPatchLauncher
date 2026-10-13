namespace PawsPatchLauncher.Rwd;

public static class RwdOperations
{
    public static (int Written, int Kept) Extract(string archivePath, string folder, bool overwrite = false)
    {
        using var archive = RwdArchive.Open(archivePath);
        var root = Path.GetFullPath(folder);
        using var rootGuard = RwdFileSystem.LockDirectory(root, create: true);
        var targets = archive.Entries.Select(e => (Entry: e, Path: Destination(root, e.Name))).ToList();
        // Validate all existing components before extracting any payload. Also
        // reject file/directory collisions rather than fail halfway through.
        var names = new HashSet<string>(archive.Entries.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var (entry, target) in targets)
        {
            var segments = entry.Name.Split('/'); var prefix = "";
            for (var i = 0; i < segments.Length; i++)
            {
                prefix = prefix.Length == 0 ? segments[i] : prefix + "/" + segments[i];
                var component = Destination(root, prefix);
                RwdFileSystem.CheckLink(component);
                if (i < segments.Length - 1 && (names.Contains(prefix) || File.Exists(component)))
                    throw new InvalidDataException("Archive file/directory collision: " + prefix);
            }
            if (Directory.Exists(target)) throw new InvalidDataException("Entry targets a directory: " + entry.Name);
            if (string.Equals(Path.GetFullPath(archivePath), target, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cannot extract over the input archive.");
        }
        int written = 0, kept = 0;
        foreach (var (entry, target) in targets)
        {
            if (RwdFileSystem.AtomicWrite(target, overwrite, output => archive.CopyTo(entry, output))) written++;
            else kept++;
        }
        return (written, kept);
    }

    static string Destination(string root, string name)
    {
        var target = Path.GetFullPath(Path.Combine(root, RwdArchive.NormalizeName(name).Replace('/', Path.DirectorySeparatorChar)));
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Entry leaves the target directory.");
        return target;
    }

    public static (int Files, uint Stamp) Pack(string folder, string archivePath, uint? stamp = null)
    {
        var root = Path.GetFullPath(folder); var output = Path.GetFullPath(archivePath);
        using var rootGuard = RwdFileSystem.LockDirectory(root);
        var files = new List<KeyValuePair<string, string>>(); var pending = new Stack<string>(); pending.Push(root);
        int directories = 0;
        while (pending.Count != 0)
        {
            if (++directories > RwdArchive.MaxEntries) throw new InvalidDataException("Too many input directories.");
            var current = pending.Pop();
            using var guard = RwdFileSystem.LockDirectory(current);
            foreach (var path in Directory.EnumerateFileSystemEntries(current))
            {
                RwdFileSystem.CheckLink(path);
                if (Directory.Exists(path))
                {
                    if (directories + pending.Count == RwdArchive.MaxEntries) throw new InvalidDataException("Too many input directories.");
                    pending.Push(path);
                }
                else if (!string.Equals(Path.GetFullPath(path), output, StringComparison.OrdinalIgnoreCase))
                {
                    if (files.Count == RwdArchive.MaxEntries) throw new InvalidDataException("Too many input files.");
                    files.Add(new(Path.GetRelativePath(root, path), path));
                }
            }
        }
        return (files.Count, RwdArchive.WriteFiles(output, files, stamp));
    }
}
