using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using SharpCompress.Archives;

namespace PawsPatchLauncher;

// Builds only the signed, enumerated module files. No source-archive path is
// ever extracted to disk, no archive code is run, and no game files are touched.
public static class ArcaneSourceAssembly
{
    public const string OriginalPackageHash = "D187E0DE5920F591CF8668616CC036DCE6D261ECC436C68EBC657C565E2A049C";
    public const string AssembledPackageHash = "E562BE5AF62752D61DB27306B4C7623CF30C37F916EB199DF9C5E4CFA0F9FE19";

    // These two reviewed archives contain the same manifest and all 6,599 payload
    // files. Normalize only network identity; downloads still verify their real hash.
    public static string CompatibilityHash(string id, string sha256) =>
        id.Equals("arcane-wars", StringComparison.OrdinalIgnoreCase) && sha256.Equals(AssembledPackageHash, StringComparison.OrdinalIgnoreCase)
            ? OriginalPackageHash : sha256;

    public static void Build(string baseRar, string patchBundle, string output, string version, CancellationToken ct = default)
    {
        using var bundle = ZipFile.OpenRead(patchBundle);
        if (bundle.Entries.Count != 3 || bundle.Entries.Select(e => e.FullName).Distinct(StringComparer.Ordinal).Count() != 3)
            throw new InvalidDataException("Invalid Arcane Wars patch bundle.");
        byte[] Read(string name, int maximum)
        {
            var entry = bundle.GetEntry(name) ?? throw new InvalidDataException("Missing patch bundle entry: " + name);
            if (entry.Length > maximum) throw new InvalidDataException("Patch bundle entry is too large.");
            using var input = entry.Open(); using var data = new MemoryStream();
            var buffer = new byte[8192]; int n;
            while ((n = input.Read(buffer)) > 0) { ct.ThrowIfCancellationRequested(); if (data.Length+n > maximum) throw new InvalidDataException("Patch bundle entry is too large."); data.Write(buffer,0,n); }
            return data.ToArray();
        }
        var manifestBytes = Read("module.json", 4 * 1024 * 1024);
        _ = Read("credits.txt", 65536);
        var module = JsonSerializer.Deserialize(manifestBytes, LauncherJsonContext.Default.ModuleArchiveManifest)
            ?? throw new InvalidDataException("Missing Arcane Wars manifest.");
        if (module.SchemaVersion != 1 || module.Id != "arcane-wars" || module.Version != version || module.Remove.Count != 0
            || module.Files.Count == 0 || module.Files.Count > 20000)
            throw new InvalidDataException("Unexpected Arcane Wars manifest identity.");
        using var patchData = new MemoryStream(Read("darquan-patch.rar", 8 * 1024 * 1024), false);
        using var original = ArchiveFactory.OpenArchive(baseRar);
        using var patch = ArchiveFactory.OpenArchive(patchData);
        var baseEntries = Index(original, "Arcane Wars 0.82beta/");
        var patchEntries = Index(patch, "Patch - 0.82.1.8Beta/");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in module.Files)
        {
            var name = SafeName(file.Path);
            if (!names.Add(name) || file.Size < 0 || file.Size > 64*1024*1024 || (total = checked(total+file.Size)) > 512L*1024*1024)
                throw new InvalidDataException("Invalid Arcane Wars file list.");
        }
        using var target = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var zip = new ZipArchive(target, ZipArchiveMode.Create);
        ZipArchiveEntry Create(string name)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.NoCompression);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            entry.ExternalAttributes = 0;
            return entry;
        }
        using (var stream = Create("module.json").Open()) stream.Write(manifestBytes);
        foreach (var file in module.Files.OrderBy(f => SafeName(f.Path), StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var name = SafeName(file.Path);
            if (!patchEntries.TryGetValue(name, out var entry) && !baseEntries.TryGetValue(name, out entry))
                throw new InvalidDataException("Arcane Wars source file missing: " + name);
            if (entry.Size != file.Size) throw new InvalidDataException("Arcane Wars source size mismatch: " + name);
            using var input = entry.OpenEntryStream();
            using var dest = Create("payload/" + name).Open();
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[65536]; long received = 0; int n;
            while ((n = input.Read(buffer)) != 0)
            {
                ct.ThrowIfCancellationRequested();
                if ((received += n) > file.Size) throw new InvalidDataException("Arcane Wars source expanded beyond its declared size.");
                hash.AppendData(buffer, 0, n); dest.Write(buffer, 0, n);
            }
            if (received != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Arcane Wars source hash mismatch: " + name);
        }
    }

    private static Dictionary<string, IArchiveEntry> Index(IArchive archive, string prefix)
    {
        var result = new Dictionary<string, IArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (entry.IsEncrypted || !string.IsNullOrEmpty(entry.LinkTarget)) throw new InvalidDataException("Linked/encrypted source entry rejected.");
            if (entry.IsDirectory) continue;
            var key = SafeName(entry.Key ?? "");
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unexpected source archive root.");
            if (!result.TryAdd(SafeName(key[prefix.Length..]), entry)) throw new InvalidDataException("Duplicate source archive entry.");
            if (result.Count > 20000) throw new InvalidDataException("Too many source archive files.");
        }
        return result;
    }

    private static string SafeName(string path)
    {
        var name = path.Replace('\\', '/');
        if (name.Length == 0 || name.Split('/').Any(p => p.Length == 0 || p is "." or ".." || p.EndsWith(' ') || p.EndsWith('.')
            || p.Any(c => c < 32 || "<>:\"|?*".Contains(c)))) throw new InvalidDataException("Invalid source archive path.");
        return name;
    }
}
