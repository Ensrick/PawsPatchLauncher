using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using PawsPatchLauncher;

internal static class ArcaneSourceTests
{
    internal static async Task<int> RunAsync()
    {
        var n = 0;
        void Check(bool value) { if (!value) throw new Exception("Arcane source test failed: " + n); n++; }
        var start = ModDbDownload.ArcaneStart;
        Check(ArcaneSourceAssembly.CompatibilityHash("arcane-wars", ArcaneSourceAssembly.AssembledPackageHash) == ArcaneSourceAssembly.OriginalPackageHash);
        Check(ArcaneSourceAssembly.CompatibilityHash("arcane-wars", "changed") == "changed");
        Check(ArcaneSourceAssembly.CompatibilityHash("other", ArcaneSourceAssembly.AssembledPackageHash) == ArcaneSourceAssembly.AssembledPackageHash);
        var mirror = "https://www.moddb.com/downloads/mirror/249627/130/abc123";
        Check(ModDbDownload.ParseMirror(start, "<a href='/downloads/mirror/249627/130/abc123'>download</a>") == mirror);
        Check(ModDbDownload.ParseMirror(start, "<a href=\"" + mirror + "\">download</a>") == mirror);
        foreach (var bad in new[] { "https://evil.invalid/downloads/mirror/249627/130/abc123", "/downloads/mirror/123/130/abc123", "http://www.moddb.com/downloads/mirror/249627/130/abc123", "https://www.moddb.com:444/downloads/mirror/249627/130/abc123", "//www.moddb.com.evil.invalid/downloads/mirror/249627/130/abc123", "/downloads/mirror/249627/130/abc123?redirect=bad" })
        {
            try { ModDbDownload.ParseMirror(start, "<a href='" + bad + "'>download</a>"); Check(false); }
            catch (Exception e) when (e is IOException or InvalidDataException) { Check(true); }
        }
        foreach (var bad in new[] { "http://www.moddb.com/downloads/start/249627", start+"?x=1", start+"#x", "https://user@www.moddb.com/downloads/start/249627", "https://www.moddb.com:444/downloads/start/249627" }) Check(!ModDbDownload.IsStartUrl(bad));
        var responses = new[] { new string('x', 1024*1024+1), "<html>Please verify you are human</html>" };
        foreach (var html in responses)
        {
            using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent(html) }));
            try { await ModDbDownload.ResolveAsync(http, start, default); Check(false); }
            catch (Exception e) when (e is IOException or InvalidDataException) { Check(true); }
        }
        using (var cancel = new CancellationTokenSource())
        using (var http = new HttpClient(new Handler(_ => throw new Exception("Cancelled request reached server"))))
        {
            cancel.Cancel();
            try { await ModDbDownload.ResolveAsync(http, start, cancel.Token); Check(false); }
            catch (OperationCanceledException) { Check(true); }
        }
        var root = Path.Combine(Path.GetTempPath(), "PawsArcaneSourceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var wrong = Path.Combine(root, "wrong.rar"); await File.WriteAllTextAsync(wrong, "wrong file");
            var package = new PackageRelease { Id = "arcane-wars", Version = "test", Sha256 = new string('0',64), Size = 100,
                SourceRecipe = new() { Base = new() { Url = start, Size = 10, Sha256 = new string('1',64) }, Patch = new() { Url = wrong, Size = 10, Sha256 = new string('2',64) } } };
            int prompts = 0;
            using var http = new HttpClient(new Handler(_ => new(HttpStatusCode.Forbidden)));
            var client = new FeedClient(new LauncherConfiguration { CacheRoot = Path.Combine(root,"cache") },http);
            try { await client.DownloadVerifiedAsync(package, null, default, _ => { prompts++; return Task.FromResult<string?>(null); }); Check(false); }
            catch (OperationCanceledException) { Check(prompts == 1); }
            try { await client.DownloadVerifiedAsync(package, null, default, _ => { prompts++; return Task.FromResult<string?>(wrong); }); Check(false); }
            catch (InvalidDataException) { Check(prompts == 2); }
            Check(!Directory.EnumerateFiles(Path.Combine(root,"cache"),"*.bin",SearchOption.AllDirectories).Any());
            Check(!Directory.EnumerateFiles(Path.Combine(root,"cache"),"*.zip",SearchOption.AllDirectories).Any());
            package.SourceRecipe.Base.Sha256 = "../invalid";
            try { await client.DownloadVerifiedAsync(package,null); Check(false); }
            catch (InvalidDataException) { Check(true); }
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            package.SourceRecipe.Base.Sha256 = new string('1',64);
            try { await client.DownloadVerifiedAsync(package,null,cancel.Token,_ => { prompts++; return Task.FromResult<string?>(wrong); }); Check(false); }
            catch (OperationCanceledException) { Check(prompts == 2); }
            var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
            var exe = Path.Combine(game, "k2.exe"); await File.WriteAllTextAsync(exe, "fixture bytes, not executable");
            var state = new InstallState();
            state.Modules["arcane-wars"] = new InstalledModule { Version = "0.82.1.8-clean.1", ArchiveSha256 = ArcaneSourceAssembly.OriginalPackageHash };
            var settings = new UserSettings { Mod = GameMod.ArcaneWars };
            var oldReport = await MultiplayerCheck.CreateAsync(game, state, settings, exe, "1.3.72");
            state.Modules["arcane-wars"].ArchiveSha256 = ArcaneSourceAssembly.AssembledPackageHash;
            var newReport = await MultiplayerCheck.CreateAsync(game, state, settings, exe, "1.3.72");
            Check(oldReport.Fingerprint == newReport.Fingerprint);
            Check(MultiplayerDetails.Compare(oldReport.Details!,newReport.Details!).Matches);
            await File.WriteAllTextAsync(exe, "changed fixture");
            var changedReport = await MultiplayerCheck.CreateAsync(game, state, settings, exe, "1.3.72");
            Check(!MultiplayerDetails.Compare(newReport.Details!,changedReport.Details!).Matches);
        }
        finally { Directory.Delete(root,true); }
        Console.WriteLine($"ARCANE SOURCES PASS {n}: mirror identity, HTML failure, size bound, cancellation, single import prompt, corrupt import rejection.");
        return n;
    }

    internal static async Task BuildAsync(string baseRar, string bundle, string output)
    {
        using var zip = ZipFile.OpenRead(bundle);
        using var s = zip.GetEntry("module.json")!.Open();
        var module = JsonSerializer.Deserialize<ModuleArchiveManifest>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        ArcaneSourceAssembly.Build(baseRar, bundle, output, module.Version);
        Console.WriteLine(JsonSerializer.Serialize(new { size = new FileInfo(output).Length, sha256 = await CryptoAndIO.Sha256Async(output), files = module.Files.Count }));
    }

    internal static async Task IntegrationAsync(string recipePath, string root, bool live = false)
    {
        Directory.CreateDirectory(root);
        var package = JsonSerializer.Deserialize<PackageRelease>(await File.ReadAllTextAsync(recipePath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        int calls = 0;
        var originalBase = package.SourceRecipe!.Base.Url;
        if (!live) package.SourceRecipe.Base.Url = ModDbDownload.ArcaneStart;
        using var http = live ? new HttpClient() : new HttpClient(new Handler(_ => { calls++; return new(HttpStatusCode.Forbidden); }));
        http.Timeout = TimeSpan.FromMinutes(5);
        var feed = new FeedClient(new LauncherConfiguration { CacheRoot = Path.Combine(root, "cache") }, http);
        int imports = 0;
        var result = await feed.DownloadVerifiedAsync(package, null, default, live ? null : _ => { imports++; return Task.FromResult<string?>(originalBase); });
        var second = await feed.DownloadVerifiedAsync(package, null);
        if (result != second || !live && (calls != 1 || imports != 1)) throw new Exception("Verified cache was not reused.");
        var game = Path.Combine(root, "game"); Directory.CreateDirectory(game);
        var installer = new ModuleInstaller(game);
        var module = await installer.PrepareAsync(package, result);
        if (module.Files.Count != 6599 || !installer.IsPrepared(package)) throw new Exception("Real module preparation failed.");
        var sentinel = Path.Combine(game, "data", "AVars_version.tgi");
        Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
        await File.WriteAllTextAsync(sentinel, "original fixture content");
        await installer.ReconcileAsync(new Dictionary<string, InstalledModule> { [package.Id] = module });
        if ((await installer.VerifyAsync()).Count != 0) throw new Exception("Installed file verification failed.");
        await installer.UninstallAsync();
        if (await File.ReadAllTextAsync(sentinel) != "original fixture content") throw new Exception("Rollback changed original fixture content.");
        Console.WriteLine($"ARCANE REAL INSTALL PASS: {module.Files.Count} file hashes, identical cache reuse, install/verify/uninstall restored original; live={live}.");
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(reply(request)); }
    }
}
