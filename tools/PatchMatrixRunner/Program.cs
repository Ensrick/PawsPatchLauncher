using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PawsPatchLauncher;

// Explicit, resumable integration tool. Never run by the normal unit-test suite.
// Installs into its own marked fixture, starts the actual supported stock game,
// observes native menu state read-only, then terminates only its verified test
// process. Interactive gameplay/normal-exit checks are performed separately.
internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string Marker = ".paw-live-matrix.json";
    private const string OracleRevision = "native-menu-live-helper-v2";
    private static bool NightmareSmoke;
    private sealed record Case(string Id, UserSettings Settings);
    private sealed record Result(string Id, string Identity, string Channel, string Mod, string Executable,
        int Pid, double Seconds, string Phase, string HelperLog, string[] Modules, string CompletedAt, string StopMethod)
    {
        public double InstallationSeconds { get; init; }
        public double LaunchAndCleanupSeconds { get; init; }
    }
    private static readonly Dictionary<string, InstalledModule> Prepared = new();
    private static string Required(Dictionary<string,string> args, string key)
        => args.GetValueOrDefault(key) ?? throw new ArgumentException("Missing --" + key);
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static async Task Main(string[] args)
    {
        try { await Run(args); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
    }
    private static async Task Run(string[] args)
    {
        var options = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var arg in args)
        {
            var pair = arg.TrimStart('-').Split('=', 2);
            if (!arg.StartsWith("--") || pair.Length != 2 || !options.TryAdd(pair[0], pair[1]))
                throw new ArgumentException("Use unique --name=value arguments.");
        }
        var root = Path.GetFullPath(Required(options, "root"));
        var game = Path.Combine(root, "game");
        var stock = Path.GetFullPath(Required(options, "stock"));
        if (root.StartsWith(stock + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || stock.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || root.Equals(stock, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture and installed game must be separate.");
        var mode = options.GetValueOrDefault("mode", "plan");
        if (mode is not ("plan" or "prepare" or "run" or "apply" or "observe" or "verify")) throw new ArgumentException("mode must be plan, prepare, run, apply, verify or observe.");
        Directory.CreateDirectory(root);
        if(mode=="observe")
        {
            if(!File.Exists(Path.Combine(game,Marker)))throw new InvalidOperationException("Not a test fixture.");
            Console.WriteLine(JsonSerializer.Serialize(new KohanActivityReader().Read(game,new ModuleInstaller(game).LoadState()),Json));
            return;
        }
        var configuration = new LauncherConfiguration
        {
            FeedUrls = [Path.GetFullPath(Required(options, "stable"))],
            BetaFeedUrls = [Path.GetFullPath(Required(options, "beta"))],
            PublicKeyPem = await File.ReadAllTextAsync(Required(options, "key")),
            CacheRoot = Path.Combine(root, "cache")
        };
        var client = new FeedClient(configuration);
        var feeds = new Dictionary<string, ChannelManifest>();
        foreach (var channel in new[] { "stable", "beta" })
            feeds[channel] = await client.GetChannelAsync(channel) ?? throw new InvalidDataException("Missing " + channel);
        NightmareSmoke = options.GetValueOrDefault("nightmare-smoke") == "true";
        var cases = (options.GetValueOrDefault("pure-stable-smoke") == "true" ? PureStableCases() : options.GetValueOrDefault("pure-beta-smoke") == "true" ? PureBetaCases() : NightmareSmoke ? NightmareCases() : Cases()).Where(c => !options.TryGetValue("channel", out var channel) || c.Settings.Channel == channel)
            .Where(c => !options.TryGetValue("mod", out var mod) || c.Settings.Mod == mod)
            .Where(c => !options.TryGetValue("case", out var id) || c.Id == id).ToArray();
        if(mode=="apply"&&(!options.ContainsKey("case")||cases.Length!=1))throw new ArgumentException("Apply requires one exact --case.");
        await File.WriteAllTextAsync(Path.Combine(root, "cases.json"), JsonSerializer.Serialize(cases, Json));
        Console.WriteLine($"PLAN {cases.Length}: mod/channel/component/text/voice combinations; {game}");
        if (mode == "plan") return;
        EnsureNoGame();
        await PrepareFixture(game, stock);
        var installer = new ModuleInstaller(game);
        foreach (var item in cases)
        foreach (var package in GamePackageSelector.Select(feeds[item.Settings.Channel], EffectiveSettings.ForFeed(item.Settings, feeds[item.Settings.Channel]),
            item.Settings.RussianLocalization, EffectiveSettings.ForFeed(item.Settings, feeds[item.Settings.Channel]).CustomPlayerColors))
        {
            var key = package.Id + ":" + package.Sha256;
            if (Prepared.ContainsKey(key)) continue;
            Prepared[key] = await installer.PrepareAsync(package, await client.DownloadVerifiedAsync(package, null));
        }
        await File.WriteAllTextAsync(Path.Combine(root, "prepared.json"), JsonSerializer.Serialize(new
        {
            game, packages = Prepared.Count, feeds = feeds.ToDictionary(f => f.Key, f => ChannelFingerprint.Create(f.Value))
        }, Json));
        if (mode == "prepare") return;
        var resultsFile = Path.Combine(root, "launches.jsonl");
        var completed = new HashSet<string>(StringComparer.Ordinal);
        if (File.Exists(resultsFile))
            foreach (var line in await File.ReadAllLinesAsync(resultsFile))
                if (!string.IsNullOrWhiteSpace(line)) completed.Add(JsonSerializer.Deserialize<Result>(line, Json)!.Identity);
        var limit = int.Parse(options.GetValueOrDefault("limit", "2147483647"));
        if (cases.Length == 0) throw new ArgumentException("No cases match the requested filters.");
        var done = 0; var skipped = 0;
        foreach (var item in cases)
        {
            if (File.Exists(Path.Combine(root, "stop.requested"))) { Console.WriteLine("STOP requested between cases"); break; }
            if (done >= limit) break;
            var feed = feeds[item.Settings.Channel];
            var settings = EffectiveSettings.ForFeed(item.Settings, feed);
            var packages = GamePackageSelector.Select(feed, settings, settings.RussianLocalization, settings.CustomPlayerColors);
            var executable = GameExecutableSelector.Select(configuration, settings, feed);
            var identity = Hash(Encoding.UTF8.GetBytes(OracleRevision + "\n" + item.Id + "\n" + executable + "\n" + string.Join('\n', packages.OrderBy(p => p.Id)
                .Select(p => p.Id + "|" + p.Version + "|" + p.Sha256 + "|" + p.Priority))));
            if (mode=="run"&&completed.Contains(identity)) { skipped++; continue; }
            EnsureNoGame();
            var installTimer = Stopwatch.StartNew();
            var modules = packages.ToDictionary(p => p.Id, p => Prepared[p.Id + ":" + p.Sha256], StringComparer.OrdinalIgnoreCase);
            await installer.ReconcileAsync(modules, settings: settings, releaseId: ChannelFingerprint.Create(feed),
                gameRequirement: GameMod.Requirement(feed, settings.Mod), baseGameSha256: KohanActivityReader.SupportedSha256);
            await GameMenuMetadata.WriteAsync(game, feed, settings);
            // ReconcileAsync verifies every installed payload before committing
            // its transaction. Only generated menu metadata is written afterward;
            // check it here without hashing the entire payload a third time.
            if (await File.ReadAllTextAsync(Path.Combine(game, GameMenuMetadata.FileName)) != GameMenuMetadata.Create(feed, settings))
                throw new InvalidDataException(item.Id + ": generated menu metadata mismatch");
            var expected = MultiplayerCheck.Expected(installer.LoadState());
            var executablePath = Path.Combine(game, executable);
            var expectedHash = executable == "k2.exe" ? KohanActivityReader.SupportedSha256 : expected[executable]!.Sha256;
            if (!string.Equals(await CryptoAndIO.Sha256Async(executablePath), expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Wrong selected helper: " + executable);
            installTimer.Stop();
            if (mode == "verify")
            {
                // Read-only preflight checks the complete installed beta before
                // any native process exists. Reconcile also verifies rollback.
                if (settings.PureBetaFeatures)
                {
                    using var preflight = Process.Start(new ProcessStartInfo(executablePath)
                    { ArgumentList = { "--preflight", game }, UseShellExecute = false, CreateNoWindow = true,
                        RedirectStandardOutput = true, RedirectStandardError = true })!;
                    string output = await preflight.StandardOutput.ReadToEndAsync();
                    string error = await preflight.StandardError.ReadToEndAsync();
                    await preflight.WaitForExitAsync();
                    if (preflight.ExitCode != 0 || !output.Contains("PURE_PREFLIGHT_PASS")) throw new InvalidDataException(error + output);
                }
                done++;
                Console.WriteLine("INSTALL_PASS " + item.Id + " " + packages.Count);
                continue;
            }
            if(mode=="apply")
            {
                await File.WriteAllTextAsync(Path.Combine(root,"interactive.json"),JsonSerializer.Serialize(new {item.Id,identity,executable,executablePath,expectedHash,settings},Json));
                Console.WriteLine("APPLIED for interactive testing: "+item.Id+"; "+executablePath);return;
            }
            var launchTimer = Stopwatch.StartNew();
            var result = await Launch(item, identity, root, game, executable, packages, installer.LoadState());
            result = result with { InstallationSeconds = installTimer.Elapsed.TotalSeconds, LaunchAndCleanupSeconds = launchTimer.Elapsed.TotalSeconds };
            await File.AppendAllTextAsync(resultsFile, JsonSerializer.Serialize(result, new JsonSerializerOptions(Json) { WriteIndented = false }) + "\n");
            completed.Add(identity); done++;
            Console.WriteLine($"PASS {done + skipped}/{cases.Length} {item.Id} apply {result.InstallationSeconds:F2}s; menu {result.Seconds:F2}s; launch/exit {result.LaunchAndCleanupSeconds:F2}s pid={result.Pid}");
        }
        if (!string.Equals(await CryptoAndIO.Sha256Async(Path.Combine(game, "k2.exe")), KohanActivityReader.SupportedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Stock executable changed in fixture.");
        await File.WriteAllTextAsync(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(new
        {
            planned = cases.Length, newlyCompleted = done, reused = skipped, complete = done + skipped == cases.Length,
            stockSha256 = KohanActivityReader.SupportedSha256,
            note = mode == "verify" ? "Installation, rollback, helper selection and read-only preflight. No game process launched."
                : "Actual native main-menu launches. Test processes stop after observation; interactive match/normal-exit acceptance is separate."
        }, Json));
        Console.WriteLine($"COMPLETE {done + skipped}/{cases.Length}; new={done}, reused={skipped}");
    }
    private static IEnumerable<Case> Cases()
    {
        foreach (var channel in new[] { "stable", "beta" })
        foreach (var mod in new[] { GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars })
        foreach (var patch in new[] { false, true })
        foreach (var russian in new[] { false, true })
        foreach (var voice in new[] { "en", "ru" })
        {
            var variants = mod == GameMod.ArcaneWars && patch ? 64 : 1;
            foreach (var spawn in mod == GameMod.ArcaneWars && patch ? new[] { "standard", "x2", "x4" } : new[] { "standard" })
            for (var bits = 0; bits < variants; bits++)
            {
                bool Bit(int bit) => (bits & (1 << bit)) != 0;
                var settings = new UserSettings { Mod = mod, Channel = channel, RussianLocalization = russian, GameVoiceLanguage = voice,
                    PawPatchEnabled = patch, VanillaPawPatchEnabled = patch, ImmortalsPawPatchEnabled = patch, LargeMapSizes = patch,
                    CustomPlayerColors = Bit(0), DesyncMode = Bit(1) ? "continue" : "official", IndependentHostility = Bit(2),
                    AdditionalRoamingCompanies = Bit(3), SiegeBalance = Bit(4), DisablePowersAndShards = Bit(5), RoamingSpawnMode = spawn };
                yield return new Case($"{mod}-{channel}-patch{(patch ? 1 : 0)}-{(russian ? "ru" : "en")}-{voice}-{spawn}-{bits:D2}", settings);
            }
        }
    }
    private static IEnumerable<Case> PureBetaCases()
    {
        foreach (string mod in new[] { GameMod.Vanilla, GameMod.Immortals })
        {
            foreach (var (variant, language) in new[] { (0,"en"),(1,"ru"),(2,"de"),(3,"fr"),(4,"cs"),(5,"uk") })
            {
                var settings = new UserSettings { Mod=mod, Channel="beta", VanillaPawPatchEnabled=true, ImmortalsPawPatchEnabled=true, GameVoiceLanguage="en" };
                GameLanguages.SetText(settings,language);
                var pure=GameMod.PureComponents(settings);
                pure.Colors=(variant&1)!=0; pure.IgnoreDesync=(variant&2)!=0;
                pure.ImprovedAi=variant!=4;pure.WoundedLairDefenders=variant!=4;
                pure.IndependentHostility=variant%2!=0;pure.AdditionalRoamingCompanies=variant%2!=0;
                pure.RoamingSpawnMode=new[]{"standard","x2","x4"}[variant%3];
                yield return new Case(mod+"-pure-beta-"+language,settings);
            }
            yield return new Case(mod+"-pure-stable-rollback",new UserSettings { Mod=mod,Channel="stable",VanillaPawPatchEnabled=true,ImmortalsPawPatchEnabled=true,GameVoiceLanguage="en" });
            yield return new Case(mod+"-pure-disabled",new UserSettings { Mod=mod,Channel="beta",VanillaPawPatchEnabled=false,ImmortalsPawPatchEnabled=false,GameVoiceLanguage="en" });
        }
    }
    private static IEnumerable<Case> PureStableCases()
    {
        foreach (var item in PureBetaCases())
        {
            if (!item.Id.Contains("-pure-beta-")) continue;
            item.Settings.Channel="stable";
            yield return new Case(item.Id.Replace("-pure-beta-","-pure-stable-"),item.Settings);
        }
        foreach (var mod in new[]{GameMod.Vanilla,GameMod.Immortals})
        {
            yield return new Case(mod+"-pure-beta-promoted",new UserSettings{Mod=mod,Channel="beta",VanillaPawPatchEnabled=true,ImmortalsPawPatchEnabled=true,GameVoiceLanguage="en"});
            yield return new Case(mod+"-pure-stable-disabled",new UserSettings{Mod=mod,Channel="stable",VanillaPawPatchEnabled=false,ImmortalsPawPatchEnabled=false,GameVoiceLanguage="en"});
        }
    }
    private static IEnumerable<Case> NightmareCases()
    {
        foreach (var (id, enabled) in new[] { ("nightmare-on", true), ("nightmare-off", false), ("nightmare-on-again", true) })
        {
            var settings = new UserSettings { Mod = GameMod.ArcaneWars, Channel = "beta", PawPatchEnabled = true,
                ImprovedAi = enabled, GameVoiceLanguage = "en" };
            GameLanguages.SetText(settings, "ru");
            yield return new Case(id, settings);
        }
    }
    private static void EnsureNoGame()
    {
        var processes = Process.GetProcessesByName("k2");
        try
        {
            foreach (var process in processes)
            {
                try { if (process.HasExited) continue; }
                catch (InvalidOperationException) { continue; }
                throw new InvalidOperationException("An existing Kohan II is running; it will not be interrupted.");
            }
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private static async Task PrepareFixture(string game, string stock)
    {
        if (Directory.Exists(game))
        {
            if (!File.Exists(Path.Combine(game, Marker))) throw new InvalidOperationException("Unmarked existing directory; refusing to use as a test fixture.");
            using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(game, Marker)));
            if (!string.Equals(marker.RootElement.GetProperty("game").GetString(), game, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(marker.RootElement.GetProperty("stock").GetString(), stock, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture marker does not match requested locations.");
            return;
        }
        if (!string.Equals(await CryptoAndIO.Sha256Async(Path.Combine(stock, "k2.exe")), KohanActivityReader.SupportedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Unsupported stock game.");
        Directory.CreateDirectory(game);
        foreach (var source in Directory.EnumerateFiles(stock))
            if (Path.GetFileName(source).Equals("k2.exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(source).ToLowerInvariant() is ".rwd" or ".dll")
                File.Copy(source, Path.Combine(game, Path.GetFileName(source)));
        var sound = Path.Combine(stock, "mss");
        if (Directory.Exists(sound)) foreach (var source in Directory.EnumerateFiles(sound, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(game, "mss", Path.GetRelativePath(sound, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        // The current Steam build ships loose bootstrap/options files in addition
        // to its archives. Use the launcher's verified stock baseline, never the
        // user's modded data directory, when creating the clean fixture.
        var assembly = typeof(ModuleInstaller).Assembly;
        var baseline = (IReadOnlyDictionary<string,string>)assembly.GetType("PawsPatchLauncher.SteamVanillaBaseline")!
            .GetField("Files", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        foreach (var (relative, expected) in baseline)
        {
            byte[] bytes;
            if (Path.GetExtension(relative).Equals(".rwd", StringComparison.OrdinalIgnoreCase))
                bytes = await File.ReadAllBytesAsync(CryptoAndIO.SafeChildPath(stock, relative));
            else
            {
                using var stream = assembly.GetManifestResourceStream("PawsPatchLauncher.Assets.Vanilla1372." + Path.GetFileName(relative))
                    ?? throw new InvalidDataException("Missing verified stock resource: " + relative);
                using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer); bytes = buffer.ToArray();
            }
            if (Hash(bytes) != expected) throw new InvalidDataException("Stock baseline mismatch: " + relative);
            var target = CryptoAndIO.SafeChildPath(game, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); await File.WriteAllBytesAsync(target, bytes);
        }
        // QA-only development context for this isolated genuine Steam executable.
        // This file never enters a package or the user's installed game.
        await File.WriteAllTextAsync(Path.Combine(game, "steam_appid.txt"), "97130\n", Encoding.ASCII);
        await File.WriteAllTextAsync(Path.Combine(game, Marker), JsonSerializer.Serialize(new { game, stock, created = DateTimeOffset.UtcNow }, Json));
    }
    private static async Task<Result> Launch(Case item, string identity, string root, string gameRoot, string executable,
        List<PackageRelease> packages, InstallState state)
    {
        EnsureNoGame();
        if (File.Exists(Path.Combine(root, "stop.requested")))
            throw new OperationCanceledException("Test stop requested before launch.");
        var beforeLogs = Directory.EnumerateFiles(gameRoot, "*status*.txt").ToDictionary(p => p, p => new FileInfo(p).Length);
        var start = DateTime.UtcNow;
        var timer = Stopwatch.StartNew();
        Process? game = null; DateTime gameStarted = default;
        using var helper = Process.Start(new ProcessStartInfo(Path.Combine(gameRoot, executable))
        {
            WorkingDirectory = gameRoot, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        }) ?? throw new InvalidOperationException("Cannot start selected game helper.");
        string logs = ""; string? observedPhase=null; bool helperReady=false; bool responding=false;
        try
        {
            var reader = new KohanActivityReader();
            var stableSamples = 0;
            while (timer.Elapsed < TimeSpan.FromSeconds(45))
            {
                if (game is null)
                {
                    var candidates = Process.GetProcessesByName("k2");
                    foreach (var candidate in candidates)
                    {
                        try
                        {
                            if (candidate.HasExited) { candidate.Dispose(); continue; }
                            var actual = candidate.MainModule?.FileName;
                            var created = candidate.StartTime.ToUniversalTime();
                            if (created < start.AddMilliseconds(-100) || !string.Equals(actual, Path.Combine(gameRoot, "k2.exe"), StringComparison.OrdinalIgnoreCase))
                                throw new InvalidOperationException("A different game process appeared; test will not touch it.");
                            if (game is not null) throw new InvalidOperationException("More than one native test game appeared.");
                            game = candidate; gameStarted = created;
                        }
                        catch (System.ComponentModel.Win32Exception) { candidate.Dispose(); }
                        catch { candidate.Dispose(); throw; }
                    }
                }
                // Bind the fresh native child before observing cancellation,
                // including while it has not created a window yet. Otherwise
                // terminating its helper could leave the test child behind.
                if (game is not null && File.Exists(Path.Combine(root, "stop.requested")))
                    throw new OperationCanceledException("Test stop requested.");
                // Process caches Responding/MainWindowHandle until Refresh.
                // A single busy initialization sample must not last forever.
                game?.Refresh();
                if (game?.HasExited == true) throw new InvalidOperationException("Game exited during startup: " + game.ExitCode);
                // These launch helpers monitor the native game for its entire
                // lifetime. Earlier READY output cannot excuse a later failure.
                if (executable != "k2.exe" && helper.HasExited)
                    throw new InvalidOperationException("Selected helper exited during startup: " + helper.ExitCode);
                logs = ReadNewLogs(gameRoot, beforeLogs);
                if (logs.Contains("PURE_START_FAILED", StringComparison.Ordinal) || logs.Contains("ERROR ", StringComparison.Ordinal))
                    throw new InvalidOperationException("Helper startup failed; see captured log.");
                var ready = executable == "k2.exe" ? true : executable == "k2_paws_menu_1372.exe" ? logs.Contains("MENU_ONLY r1", StringComparison.Ordinal)
                    : executable.StartsWith("k2_paws_pure_",StringComparison.Ordinal) ? logs.Contains("PURE_PATCH_APPLIED", StringComparison.Ordinal)
                    : logs.Contains("QUIET_READY", StringComparison.Ordinal);
                var needsTransfer = item.Settings.Channel == "beta" && GameMod.PawPatchSelected(item.Settings);
                if (needsTransfer) ready &= logs.Contains("FAST_TRANSFER_R2_READY", StringComparison.Ordinal);
                if (packages.Any(p => p.Id == "pure-fixes-runtime" && p.Version == "1.3.72-pure.15-beta.1"))
                    ready &= logs.Contains("MATCH_RECOVERY_READY", StringComparison.Ordinal);
                var phase = game is null ? null : reader.Read(gameRoot, state)?.Phase;
                observedPhase=phase;helperReady=ready;responding=game is not null && game.Responding;
                if (phase == "menu" && ready && game!.MainWindowHandle != IntPtr.Zero && responding) stableSamples++;
                else stableSamples = 0;
                if (stableSamples >= 3)
                {
                    if (NightmareSmoke)
                    {
                        var definitions = NightmareDefinitionProbe.Read(game!);
                        var expectedNames = new[] { "handicap_tutor", "handicap_easy", "handicap_medium", "handicap_hard", "handicap_impossible" }
                            .Concat(item.Settings.ImprovedAi ? new[] { "handicap_paws_nightmare" } : Array.Empty<string>()).Order().ToArray();
                        if (!definitions.Order().SequenceEqual(expectedNames)) throw new InvalidDataException("Unexpected live handicaps: " + string.Join(",", definitions));
                        await File.WriteAllTextAsync(Path.Combine(root, item.Id + "-definitions.json"), JsonSerializer.Serialize(new
                        { passed = true, pid = game!.Id, aiEnabled = item.Settings.ImprovedAi, definitions, access = "QUERY_LIMITED_INFORMATION | VM_READ", lobbyEdited = false }, Json));
                    }
                    var evidence = Path.Combine(root, "logs", item.Id + ".txt"); Directory.CreateDirectory(Path.GetDirectoryName(evidence)!);
                    await File.WriteAllTextAsync(evidence, logs);
                    return new Result(item.Id, identity, item.Settings.Channel, item.Settings.Mod, executable, game!.Id,
                        timer.Elapsed.TotalSeconds, phase!, Path.GetRelativePath(root, evidence), packages.Select(p => p.Id + "@" + p.Version).ToArray(),
                        DateTimeOffset.UtcNow.ToString("O"), "verified test process termination after native menu observation");
                }
                if (helper.HasExited && game is null) throw new InvalidOperationException("Selected helper exited without a native game: " + helper.ExitCode);
                await Task.Delay(150);
            }
            throw new TimeoutException("Native main menu / helper readiness did not appear in 45 seconds.");
        }
        catch (Exception error)
        {
            try
            {
                object[] nativeLogs = []; string? nativeLogReadError = null;
                try
                {
                    nativeLogs = Directory.EnumerateFiles(Path.Combine(gameRoot,"Logs"),"log-*.log")
                        .OrderByDescending(File.GetLastWriteTimeUtc).Take(1)
                        .Select(p=>new {file=Path.GetFileName(p),tail=ReadLiveLogTail(p)}).ToArray();
                }
                catch (Exception readError) { nativeLogReadError = readError.ToString(); }
                await File.WriteAllTextAsync(Path.Combine(root, "failure.json"), JsonSerializer.Serialize(new { item.Id, identity, executable, error = error.ToString(), observedPhase,helperReady,responding,logs,nativeLogs,nativeLogReadError }, Json));
            }
            catch (Exception diagnosticError)
            {
                try { Console.Error.WriteLine("Failure diagnostics unavailable: " + diagnosticError); }
                catch { } // Reporting a diagnostic failure must preserve the primary exception too.
            }
            throw;
        }
        finally
        {
            try
            {
                if (game is not null)
                {
                    try
                    {
                        game.Refresh();
                        if (!game.HasExited && game.StartTime.ToUniversalTime() == gameStarted
                            && string.Equals(game.MainModule?.FileName, Path.Combine(gameRoot, "k2.exe"), StringComparison.OrdinalIgnoreCase))
                        { game.Kill(); await game.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
                    }
                    finally { game.Dispose(); }
                }
            }
            finally
            {
                // A failed native stop must not skip the directly started helper.
                try
                {
                    if (!helper.HasExited)
                    {
                        try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
                        catch (TimeoutException)
                        {
                            if (!helper.HasExited) helper.Kill();
                            await helper.WaitForExitAsync();
                        }
                    }
                }
                finally
                {
                    // It can no longer create another child. Cover a child that
                    // appeared after the last poll or while awaiting helper exit.
                    if (helper.HasExited)
                        await StopLateNativeGameAsync(gameRoot, start, helper.ExitTime.ToUniversalTime());
                }
            }
        }
    }
    private static async Task StopLateNativeGameAsync(string gameRoot, DateTime launchStarted, DateTime helperExited)
    {
        var candidates = Process.GetProcessesByName("k2");
        Process? game = null; DateTime gameStarted = default;
        var expectedPath = Path.Combine(gameRoot, "k2.exe");
        try
        {
            // Validate the whole discovery before stopping anything. A different,
            // unreadable or ambiguous process is never treated as our late child.
            foreach (var candidate in candidates)
            {
                if (candidate.HasExited) continue;
                var actual = candidate.MainModule?.FileName;
                var created = candidate.StartTime.ToUniversalTime();
                if (created < launchStarted.AddMilliseconds(-100) || created > helperExited
                    || !string.Equals(actual, expectedPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("A different game process appeared during cleanup; test will not touch it.");
                if (game is not null) throw new InvalidOperationException("More than one native test game appeared during cleanup.");
                game = candidate; gameStarted = created;
            }
            if (game is not null)
            {
                game.Refresh();
                if (!game.HasExited && game.StartTime.ToUniversalTime() == gameStarted
                    && string.Equals(game.MainModule?.FileName, expectedPath, StringComparison.OrdinalIgnoreCase))
                { game.Kill(); await game.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
            }
        }
        finally { foreach (var candidate in candidates) candidate.Dispose(); }
    }
    private static string[] ReadLiveLogTail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var tail = new Queue<string>(35);
        while (reader.ReadLine() is { } line)
        {
            if (tail.Count == 35) tail.Dequeue();
            tail.Enqueue(line);
        }
        return tail.ToArray();
    }
    private static string ReadNewLogs(string root, IReadOnlyDictionary<string,long> previous)
    {
        var text = new StringBuilder();
        foreach (var path in Directory.EnumerateFiles(root, "*status*.txt"))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Position = Math.Min(previous.GetValueOrDefault(path), stream.Length);
            using var reader = new StreamReader(stream); text.AppendLine(reader.ReadToEnd());
        }
        return text.ToString();
    }
}
