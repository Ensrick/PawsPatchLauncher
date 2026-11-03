using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace PawsPatchLauncher;

public sealed class FeedClient
{
    private readonly HttpClient _http;
    private readonly LauncherConfiguration _configuration;
    private readonly string _cacheRoot;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (long Length, DateTime Written, ChannelManifest Manifest)> _verifiedArchives = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ChannelManifest> _knownChannels = new(StringComparer.OrdinalIgnoreCase);
    public ChannelManifest? KnownChannel(string channel) => _knownChannels.GetValueOrDefault(channel);
    private void RememberChannel(ChannelManifest manifest) => _knownChannels.AddOrUpdate(manifest.Channel, manifest,
        (_, previous) => DateTimeOffset.TryParse(previous.PublishedAt, out var before) && DateTimeOffset.TryParse(manifest.PublishedAt, out var after) && before > after ? previous : manifest);
    public string CacheDirectory => _cacheRoot;

    public FeedClient(LauncherConfiguration configuration, HttpClient? http = null)
    {
        _configuration = configuration;
        _cacheRoot = string.IsNullOrWhiteSpace(configuration.CacheRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawsPatchLauncher")
            : Path.GetFullPath(configuration.CacheRoot);
        _http = http ?? new HttpClient(LocalTestNetwork.Handler(() => new HttpClientHandler())) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PawsPatchLauncher/0.2");
    }

    public Task<ChannelManifest?> GetChannelAsync(string channel = "stable", CancellationToken cancellationToken = default)
        => Task.Run(() => GetChannelCoreAsync(channel, cancellationToken), cancellationToken);

    private async Task<ChannelManifest?> GetChannelCoreAsync(string channel = "stable", CancellationToken cancellationToken = default)
    {
        var sources = channel.Equals("beta", StringComparison.OrdinalIgnoreCase)
            ? _configuration.BetaFeedUrls
            : _configuration.FeedUrls;
        if (sources.Count == 0) return null;
        var errors = new List<Exception>();
        foreach (var source in sources)
        {
            try
            {
                var bytes = await ReadBytesAsync(source, cancellationToken);
                var manifest = ParseFeed(bytes, IsRemote(source));
                if (!manifest.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Requested update channel '{channel}', received '{manifest.Channel}'.");
                await ArchiveAsync(bytes, manifest, cancellationToken);
                RememberChannel(manifest);
                return manifest;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) { errors.Add(ex); }
        }
        throw new AggregateException("Every update feed failed.", errors);
    }

    // Launcher versions are shared by all patch channels. Read both current
    // channel endpoints and all configured mirrors, never the selected/pinned
    // patch manifest. Do not archive/apply any gameplay data during this check.
    public async Task<LauncherRelease?> GetLauncherUpdateAsync(CancellationToken cancellationToken = default)
        => await GetLauncherUpdateAsync(cancellationToken, null);

    public Task<LauncherRelease?> GetLauncherUpdateAsync(CancellationToken cancellationToken, TimeSpan? sourceTimeout)
        => Task.Run(() => GetLauncherUpdateCoreAsync(cancellationToken, sourceTimeout), cancellationToken);

    private async Task<LauncherRelease?> GetLauncherUpdateCoreAsync(CancellationToken cancellationToken, TimeSpan? sourceTimeout)
    {
        var sources = _configuration.FeedUrls.Select(url => (Url: url, Channel: "stable"))
            .Concat(_configuration.BetaFeedUrls.Select(url => (Url: url, Channel: "beta"))).Distinct().ToArray();
        if (sources.Length == 0) return null;
        async Task<(LauncherRelease? Release, Exception? Error)> Read((string Url, string Channel) source)
        {
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (sourceTimeout is { } timeout) bounded.CancelAfter(timeout);
            try
            {
                var manifest = ParseFeed(await ReadBytesAsync(source.Url, bounded.Token), IsRemote(source.Url));
                if (!manifest.Channel.Equals(source.Channel, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Wrong channel in launcher update source: " + source.Url);
                var release = manifest.Launcher;
                RememberChannel(manifest);
                if (release.Version == "0.0.0" && release.Urls.Count == 0) return (null, null);
                if (!LauncherUpdateState.IsValid(release)) throw new InvalidDataException("Invalid launcher release metadata: " + source.Url);
                return (release, null);
            }
            catch (Exception error) { return (null, error); }
        }
        var results = await Task.WhenAll(sources.Select(Read));
        var latest = new LauncherUpdateState();
        foreach (var result in results) latest.Observe(result.Release);
        // A healthy source still wins when another source fails or times out.
        if (results.Any(result => result.Error is null)) return latest.Latest;
        cancellationToken.ThrowIfCancellationRequested();
        throw new AggregateException("Every launcher update source failed.", results.Select(result => result.Error!));
    }

    public Task<string> DownloadVerifiedAsync(PackageRelease package, IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken = default, Func<CancellationToken, Task<string?>>? chooseModDbArchive = null)
        => Task.Run(() => DownloadVerifiedCoreAsync(package, progress, cancellationToken, chooseModDbArchive), cancellationToken);

    private async Task<string> DownloadVerifiedCoreAsync(PackageRelease package, IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken = default, Func<CancellationToken, Task<string?>>? chooseModDbArchive = null)
    {
        var cacheRoot = Path.Combine(_cacheRoot, "downloads", package.Id, package.Version);
        Directory.CreateDirectory(cacheRoot);
        var destination = CachedPackagePath(package);
        if (File.Exists(destination) && string.Equals(await CryptoAndIO.Sha256Async(destination, cancellationToken), package.Sha256, StringComparison.OrdinalIgnoreCase))
            return destination;

        if (package.SourceRecipe is not null)
            return await DownloadAssembledAsync(package, destination, progress, cancellationToken, chooseModDbArchive);

        var errors = new List<Exception>();
        foreach (var url in package.Urls.Concat(package.Urls))
        {
            var temporary = destination + ".download";
            try
            {
                await DownloadAsync(url, temporary, progress, cancellationToken, package.Size);
                var actual = await CryptoAndIO.Sha256Async(temporary, cancellationToken);
                if (!string.Equals(actual, package.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temporary);
                    throw new InvalidDataException($"SHA-256 mismatch for {package.Id}: expected {package.Sha256}, got {actual}.");
                }
                File.Move(temporary, destination, true);
                await PackageDownloadDate.RecordAsync(destination, cancellationToken);
                return destination;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
        throw new AggregateException($"Every download mirror failed for {package.Id}.", errors);
    }

    public bool IsPackageCached(PackageRelease package)
    {
        var path = CachedPackagePath(package);
        if (!File.Exists(path)) return false;
        return package.Size <= 0 || new FileInfo(path).Length == package.Size;
    }

    private string CachedPackagePath(PackageRelease package)
        => Path.Combine(_cacheRoot, "downloads", package.Id, package.Version, package.Sha256.ToUpperInvariant() + ".zip");

    public Task<string> GetCachedPackageAsync(PackageRelease package, CancellationToken cancellationToken = default)
        => Task.Run(() => GetCachedPackageCoreAsync(package, cancellationToken), cancellationToken);

    private async Task<string> GetCachedPackageCoreAsync(PackageRelease package, CancellationToken cancellationToken = default)
    {
        var path = CachedPackagePath(package);
        if (!File.Exists(path) || !(await CryptoAndIO.Sha256Async(path, cancellationToken)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Installed mod files are missing or damaged. Install the mod again to restore them: " + package.Id);
        return path;
    }

    public Task<string> DownloadLauncherAsync(LauncherRelease release, IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken = default)
        => Task.Run(() => DownloadLauncherCoreAsync(release, progress, cancellationToken), cancellationToken);

    private async Task<string> DownloadLauncherCoreAsync(LauncherRelease release, IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(_cacheRoot, "launcher");
        Directory.CreateDirectory(directory);
        var destination = CryptoAndIO.SafeChildPath(directory, $"PawsPatchLauncher-{release.Version}-{release.Sha256}.exe");
        if (File.Exists(destination) && string.Equals(await CryptoAndIO.Sha256Async(destination, cancellationToken), release.Sha256, StringComparison.OrdinalIgnoreCase))
            return destination;

        var errors = new List<Exception>();
        foreach (var url in release.Urls.Concat(release.Urls))
        {
            var temporary = destination + ".download";
            try
            {
                await DownloadAsync(url, temporary, progress, cancellationToken, release.Size);
                var actual = await CryptoAndIO.Sha256Async(temporary, cancellationToken);
                if (!actual.Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(temporary);
                    throw new InvalidDataException($"Launcher SHA-256 mismatch: expected {release.Sha256}, got {actual}.");
                }
                File.Move(temporary, destination, true);
                return destination;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
        throw new AggregateException("Every launcher download mirror failed.", errors);
    }

    private ChannelManifest ParseFeed(byte[] bytes, bool remote)
    {
        var text = Encoding.UTF8.GetString(bytes);
        SignedFeedEnvelope? envelope = null;
        try { envelope = JsonSerializer.Deserialize(text, LauncherJsonContext.Default.SignedFeedEnvelope); } catch { }

        byte[] payload;
        if (envelope is not null && !string.IsNullOrWhiteSpace(envelope.Payload))
        {
            payload = Convert.FromBase64String(envelope.Payload);
            if (!CryptoAndIO.VerifySignature(payload, envelope.Signature, _configuration.PublicKeyPem))
                throw new CryptographicException("The update manifest signature is invalid.");
        }
        else
        {
            if (remote && _configuration.RequireSignedRemoteFeed)
                throw new CryptographicException("An unsigned remote update manifest was rejected.");
            payload = bytes;
        }

        var manifest = JsonSerializer.Deserialize(payload, LauncherJsonContext.Default.ChannelManifest)
                       ?? throw new InvalidDataException("The update manifest is empty.");
        if (manifest.SchemaVersion != 1) throw new InvalidDataException($"Unsupported update manifest schema: {manifest.SchemaVersion}.");
        return manifest;
    }

    private async Task DownloadAsync(string source, string destination, IProgress<(long Received, long? Total)>? progress,
        CancellationToken cancellationToken, long expectedSize = 0)
    {
        if (!IsRemote(source))
        {
            var local = source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(source).LocalPath : source;
            await using var input = new FileStream(local, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            await using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
            await CopyWithProgressAsync(input, output, input.Length, progress, cancellationToken);
            return;
        }

        if (ModDbDownload.IsStartUrl(source))
            source = await ModDbDownload.ResolveAsync(_http, source, cancellationToken);
        await ResumableDownload.DownloadAsync(_http, source, destination, expectedSize, progress, cancellationToken);
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> AssemblyLocks = new(StringComparer.OrdinalIgnoreCase);

    private async Task<string> DownloadAssembledAsync(PackageRelease package, string destination,
        IProgress<(long Received, long? Total)>? progress, CancellationToken ct, Func<CancellationToken, Task<string?>>? chooseModDbArchive)
    {
        var recipe = package.SourceRecipe!;
        if (package.Id != "arcane-wars" || recipe.Kind != "arcane-moddb-v1")
            throw new InvalidDataException("Unsupported source recipe.");
        foreach (var input in new[] { recipe.Base, recipe.Patch })
            if (input.Size <= 0 || input.Size > 512L * 1024 * 1024 || !System.Text.RegularExpressions.Regex.IsMatch(input.Sha256, "\\A[0-9a-fA-F]{64}\\z"))
                throw new InvalidDataException("Invalid source archive metadata.");
        var gate = AssemblyLocks.GetOrAdd(destination, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        var temporary = destination + ".download";
        try
        {
            RemovalSafety.CheckNoLinks(destination);
            RemovalSafety.CheckNoLinks(temporary);
            if (File.Exists(destination) && (await CryptoAndIO.Sha256Async(destination, ct)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                return destination;
            var directory = Path.Combine(_cacheRoot, "source-archives");
            RemovalSafety.CheckNoLinks(directory);
            Directory.CreateDirectory(directory);
            var total = checked(recipe.Base.Size + recipe.Patch.Size);
            long completed = 0;
            var paths = new List<string>();
            foreach (var input in new[] { recipe.Base, recipe.Patch })
            {
                var path = Path.Combine(directory, input.Sha256.ToUpperInvariant() + ".bin");
                RemovalSafety.CheckNoLinks(path);
                RemovalSafety.CheckNoLinks(path + ".download");
                if (!File.Exists(path) || new FileInfo(path).Length != input.Size || !(await CryptoAndIO.Sha256Async(path, ct)).Equals(input.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    var sourceProgress = new SourceProgress(p => progress?.Report((completed + p.Received, total)));
                    try { await DownloadAsync(input.Url, path + ".download", sourceProgress, ct, input.Size); }
                    catch (Exception error) when (ReferenceEquals(input, recipe.Base) && ModDbDownload.IsStartUrl(input.Url)
                        && chooseModDbArchive is not null && !ct.IsCancellationRequested && error is HttpRequestException or IOException)
                    {
                        var selected = await chooseModDbArchive(ct);
                        ct.ThrowIfCancellationRequested();
                        if (selected is null) throw new OperationCanceledException("ModDB archive selection cancelled.");
                        if (IsRemote(selected)) throw new InvalidDataException("Select a downloaded local archive.");
                        // Copy first and verify the exact bytes used by the assembler. Never trust a filename.
                        await DownloadAsync(selected, path + ".download", sourceProgress, ct, input.Size);
                    }
                    if (new FileInfo(path + ".download").Length != input.Size || !(await CryptoAndIO.Sha256Async(path + ".download", ct)).Equals(input.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(path + ".download");
                        throw new InvalidDataException("Source archive SHA-256 mismatch.");
                    }
                    File.Move(path + ".download", path, true);
                }
                paths.Add(path);
                completed += input.Size;
                progress?.Report((completed, total));
            }
            await Task.Run(() => ArcaneSourceAssembly.Build(paths[0], paths[1], temporary, package.Version, ct), ct);
            if (new FileInfo(temporary).Length != package.Size || !(await CryptoAndIO.Sha256Async(temporary, ct)).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Assembled Arcane Wars package does not match the signed catalog.");
            File.Move(temporary, destination, true);
            await PackageDownloadDate.RecordAsync(destination, ct);
            return destination;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { gate.Release(); }
        }
    }

    private sealed class SourceProgress(Action<(long Received, long? Total)> report) : IProgress<(long Received, long? Total)>
    {
        public void Report((long Received, long? Total) value) => report(value);
    }

    private static async Task CopyWithProgressAsync(Stream input, Stream output, long? total,
        IProgress<(long Received, long? Total)>? progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report((received, total));
        }
    }

    private async Task<byte[]> ReadBytesAsync(string source, CancellationToken cancellationToken)
    {
        if (!IsRemote(source))
        {
            var local = source.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? new Uri(source).LocalPath : source;
            return await File.ReadAllBytesAsync(local, cancellationToken);
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue {
            NoCache = true, MaxAge = TimeSpan.Zero };
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static bool IsRemote(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private string ArchivedPath(string id)
    {
        if (id.Length != 64 || !id.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid release fingerprint.");
        return Path.Combine(_cacheRoot, "releases", id.ToUpperInvariant() + ".json");
    }

    private async Task ArchiveAsync(byte[] bytes, ChannelManifest manifest, CancellationToken ct)
    {
        var id = ChannelFingerprint.Create(manifest);
        await CryptoAndIO.AtomicWriteTextAsync(ArchivedPath(id), Encoding.UTF8.GetString(bytes), ct);
    }

    public ChannelManifest LoadArchived(string id, string channel)
    {
        var path = ArchivedPath(id);
        var file = new FileInfo(path);
        if (_verifiedArchives.TryGetValue(path, out var cached) && file.Exists && file.Length == cached.Length && file.LastWriteTimeUtc == cached.Written)
        {
            if (cached.Manifest.Channel != channel) throw new InvalidDataException("Archived release channel mismatch.");
            return cached.Manifest;
        }
        var manifest = ParseFeed(File.ReadAllBytes(path), _configuration.RequireSignedRemoteFeed);
        if (ChannelFingerprint.Create(manifest) != id.ToUpperInvariant())
            throw new InvalidDataException("Archived release identity mismatch.");
        _verifiedArchives[path] = (file.Length, file.LastWriteTimeUtc, manifest);
        if (!manifest.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archived release channel mismatch.");
        return manifest;
    }

    public Task<ChannelManifest> GetPreviousAsync(ReleaseReference reference, string channel)
        => Task.Run(() => GetPreviousCoreAsync(reference, channel));

    private async Task<ChannelManifest> GetPreviousCoreAsync(ReleaseReference reference, string channel)
    {
        var bytes = await ReadBytesAsync(reference.Url, CancellationToken.None);
        var manifest = ParseFeed(bytes, true);
        if (!manifest.Channel.Equals(channel, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Previous release channel mismatch.");
        await ArchiveAsync(bytes, manifest, CancellationToken.None);
        return manifest;
    }

    public List<ChannelManifest> Archived(string channel)
    {
        var root = Path.Combine(_cacheRoot, "releases");
        var result = new List<ChannelManifest>();
        if (!Directory.Exists(root)) return result;
        foreach (var file in Directory.EnumerateFiles(root, "*.json"))
            try { result.Add(LoadArchived(Path.GetFileNameWithoutExtension(file), channel)); } catch { }
        return result.OrderByDescending(x => x.PublishedAt, StringComparer.Ordinal).ToList();
    }

    // Read-only fallback for documentation, not authority to install or launch an old feed.
    public ChannelManifest? CachedGuideChannel(string channel, string? pinnedRelease)
    {
        try
        {
            return pinnedRelease is null ? KnownChannel(channel) ?? Archived(channel).FirstOrDefault()
                : LoadArchived(pinnedRelease, channel);
        }
        catch { return null; }
    }
}
