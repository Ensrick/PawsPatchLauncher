using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PawsPatchLauncher;

public static class CryptoAndIO
{
    public static Task<string> Sha256Async(string path, CancellationToken cancellationToken = default)
        => Task.Run(() => Sha256CoreAsync(path, cancellationToken), cancellationToken);

    private static async Task<string> Sha256CoreAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    public static bool VerifySignature(byte[] payload, string signatureBase64, string publicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(publicKeyPem) || string.IsNullOrWhiteSpace(signatureBase64)) return false;
        using var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        return key.VerifyData(payload, Convert.FromBase64String(signatureBase64), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public static string SafeChildPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) throw new InvalidDataException("An empty package path is not allowed.");
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized)) throw new InvalidDataException($"Rooted package path is not allowed: {relativePath}");
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Package path escapes the target directory: {relativePath}");
        return full;
    }

    public static void ExtractZipSafely(string archivePath, string destination)
    {
        Directory.CreateDirectory(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var target = SafeChildPath(destination, entry.FullName);
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(target);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }
    }

    public static string NormalizeRelativePath(string path)
        => path.Replace('/', '\\').TrimStart('\\');

    public static async Task AtomicWriteTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Independent writers must never share a staging file. Readers and scanners
        // can briefly deny replacement on Windows even when the directory is writable.
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { File.Move(temporary, path, true); break; }
                catch (Exception error) when (watch.Elapsed < TimeSpan.FromSeconds(2)
                    && (error is UnauthorizedAccessException || error is IOException && (error.HResult & 0xffff) is 5 or 32 or 33))
                { await Task.Delay(100, cancellationToken).ConfigureAwait(false); }
            }
        }
        catch (UnauthorizedAccessException error)
        { throw new UnauthorizedAccessException("Cannot replace the saved file: " + path, error); }
        catch (IOException error)
        { throw new IOException("Cannot save the file atomically: " + path, error); }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}

