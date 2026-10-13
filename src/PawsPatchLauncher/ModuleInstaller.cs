using System.Text.Json;

namespace PawsPatchLauncher;

public sealed class ModuleInstaller
{
    private readonly string _gameRoot;
    private readonly string _controlRoot;
    private readonly string _statePath;
    private readonly string _backupRoot;
    private readonly string _packageRoot;

    public ModuleInstaller(string gameRoot)
    {
        _gameRoot = Path.GetFullPath(gameRoot);
        _controlRoot = Path.Combine(_gameRoot, ".pawpatch");
        _statePath = Path.Combine(_controlRoot, "state.json");
        _backupRoot = Path.Combine(_controlRoot, "originals");
        _packageRoot = Path.Combine(_controlRoot, "packages");
    }

    public InstallState LoadState()
    {
        if (File.Exists(_statePath))
        {
            var state = JsonSerializer.Deserialize(File.ReadAllText(_statePath), LauncherJsonContext.Default.InstallState)
                ?? throw new InvalidDataException("The patch installation state is damaged.");
            state.Modules = new Dictionary<string, InstalledModule>(state.Modules, StringComparer.OrdinalIgnoreCase);
            var originals = new Dictionary<string, OriginalFile>(StringComparer.OrdinalIgnoreCase);
            foreach (var (path, original) in state.Originals)
            {
                var relative = CryptoAndIO.NormalizeRelativePath(path);
                if (originals.TryGetValue(relative, out var previous)
                    && (previous.Existed != original.Existed || !string.Equals(previous.Sha256, original.Sha256, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(previous.BackupRelativePath, original.BackupRelativePath, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Conflicting original file records: " + relative);
                originals[relative] = original;
            }
            state.Originals = originals;
            return state;
        }
        return new InstallState();
    }

    public async Task RememberLegacyConfigurationAsync(UserSettings settings, string releaseId)
    {
        var state = LoadState();
        if (state.AppliedSettings is not null || state.Modules.Count == 0) return;
        state.AppliedSettings = JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, LauncherJsonContext.Default.UserSettings), LauncherJsonContext.Default.UserSettings);
        state.ReleaseId = releaseId;
        await CryptoAndIO.AtomicWriteTextAsync(_statePath, JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState));
    }

    public bool IsPrepared(PackageRelease package)
    {
        var directory = Path.Combine(_packageRoot, Sanitize(package.Id), Sanitize(package.Version));
        var marker = Path.Combine(directory, ".verified");
        return File.Exists(marker) && File.Exists(Path.Combine(directory, "module.json"))
            && File.ReadAllText(marker).Trim().Equals(package.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    public Task<InstalledModule> ReadPreparedAsync(PackageRelease package, CancellationToken cancellationToken = default)
        => PrepareAsync(package, "", cancellationToken);

    public async Task<InstalledModule> PrepareAsync(PackageRelease package, string archivePath, CancellationToken cancellationToken = default, bool force = false)
    {
        var directory = Path.Combine(_packageRoot, Sanitize(package.Id), Sanitize(package.Version));
        var readyMarker = Path.Combine(directory, ".verified");
        if (force || !IsPrepared(package))
        {
            if (string.IsNullOrEmpty(archivePath)) throw new FileNotFoundException("The installed mod component is missing: " + package.Id);
            RemovalSafety.CheckNoLinks(directory);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            Directory.CreateDirectory(directory);
            CryptoAndIO.ExtractZipSafely(archivePath, directory);
        }

        var manifestPath = Path.Combine(directory, "module.json");
        if (!File.Exists(manifestPath)) throw new InvalidDataException($"Package {package.Id} has no module.json.");
        var module = JsonSerializer.Deserialize(await File.ReadAllTextAsync(manifestPath, cancellationToken), LauncherJsonContext.Default.ModuleArchiveManifest)
                     ?? throw new InvalidDataException($"Package {package.Id} has an empty module.json.");
        if (!string.Equals(module.Id, package.Id, StringComparison.OrdinalIgnoreCase) || module.Version != package.Version)
            throw new InvalidDataException($"Package identity mismatch for {package.Id}.");

        foreach (var file in module.Files)
        {
            var source = CryptoAndIO.SafeChildPath(Path.Combine(directory, "payload"), file.Path);
            if (!File.Exists(source)) throw new InvalidDataException($"Package file is missing: {file.Path}");
            if (new FileInfo(source).Length != file.Size) throw new InvalidDataException($"Package file size mismatch: {file.Path}");
            var hash = await CryptoAndIO.Sha256Async(source, cancellationToken);
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Package file hash mismatch: {file.Path}");
        }
        var normalizedFiles = module.Files.Select(file => CryptoAndIO.NormalizeRelativePath(file.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removals = module.Remove.Select(CryptoAndIO.NormalizeRelativePath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var relative in removals)
        {
            PatchRecovery.GamePath(_gameRoot, relative);
            if (normalizedFiles.Contains(relative))
                throw new InvalidDataException($"Package {package.Id} both installs and removes: {relative}");
        }
        await File.WriteAllTextAsync(readyMarker, package.Sha256, cancellationToken);
        return new InstalledModule
        {
            Version = module.Version,
            DownloadedAt = string.IsNullOrEmpty(archivePath) ? File.GetCreationTimeUtc(readyMarker) : PackageDownloadDate.Read(archivePath),
            Priority = package.Priority,
            Enabled = true,
            ArchiveSha256 = package.Sha256,
            Files = module.Files,
            Remove = removals
        };
    }

    public async Task ReconcileAsync(IReadOnlyDictionary<string, InstalledModule> desired, CancellationToken cancellationToken = default,
        UserSettings? settings = null, string? releaseId = null, bool resetOriginals = false, bool preserveVanillaBootstrap = false,
        GameRequirement? gameRequirement = null, string? baseGameSha256 = null,
        Dictionary<string, List<CustomRwdMod>>? customMods = null, string? expectedState = null, Action? ensureGameClosed = null)
    {
        if (resetOriginals && desired.Count != 0) throw new InvalidOperationException("Originals can only be reset during uninstall.");
        Directory.CreateDirectory(_controlRoot);
        var recovery = new PatchRecovery(_gameRoot);
        await recovery.RecoverInterruptedAsync();
        var state = LoadState();
        if (expectedState is not null && JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState) != expectedState)
            throw new IOException("The installation changed. Reopen game settings and try again.");
        CustomRwdMods.Validate(state.CustomMods);
        if (settings?.DataOnly == true)
        {
            GameCompatibilityPolicy.ValidateDataModules(desired.Values);
            if (state.Modules.Values.Any(m => m.Files.Select(f => f.Path).Concat(m.Remove).Any(p => CryptoAndIO.NormalizeRelativePath(p).Equals("k2.exe", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("Restore the original game executable before applying file-only components.");
        }
        var previous = JsonSerializer.Deserialize(JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), LauncherJsonContext.Default.InstallState)!;
        state.CustomMods = CustomRwdMods.Copy(customMods ?? state.CustomMods);
        desired = await new CustomRwdMods(_gameRoot).ComposeAsync(desired, previous, state.CustomMods,
            resetOriginals ? null : settings?.Mod, cancellationToken);
        var winners = new Dictionary<string, (string Id, InstalledModule Module, ModuleFile? File)>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in desired.Where(x => x.Value.Enabled).OrderBy(x => x.Value.Priority).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var relative in pair.Value.Remove) winners[CryptoAndIO.NormalizeRelativePath(relative)] = (pair.Key, pair.Value, null);
            foreach (var file in pair.Value.Files) winners[CryptoAndIO.NormalizeRelativePath(file.Path)] = (pair.Key, pair.Value, file);
        }
        var recognized = desired.Values.SelectMany(x => x.Files).GroupBy(x => CryptoAndIO.NormalizeRelativePath(x.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Sha256).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var allPaths = state.Modules.Values.SelectMany(x => x.Files).Select(x => CryptoAndIO.NormalizeRelativePath(x.Path))
            .Concat(desired.Values.SelectMany(x => x.Files).Select(x => CryptoAndIO.NormalizeRelativePath(x.Path)))
            .Concat(state.Modules.Values.SelectMany(x => x.Remove).Select(CryptoAndIO.NormalizeRelativePath))
            .Concat(desired.Values.SelectMany(x => x.Remove).Select(CryptoAndIO.NormalizeRelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        foreach (var relative in allPaths)
        {
            if (state.Originals.ContainsKey(relative)) continue;
            var target = PatchRecovery.GamePath(_gameRoot, relative);
            var original = new OriginalFile { Existed = File.Exists(target) };
            if (original.Existed)
            {
                var actualHash = await CryptoAndIO.Sha256Async(target, cancellationToken);
                if (recognized.TryGetValue(relative, out var hashes) && hashes.Contains(actualHash)
                    && !SteamVanillaBaseline.IsOriginal(relative, actualHash))
                {
                    // A previous manual/archive installation already placed this exact managed file.
                    // Do not preserve it as a user original, otherwise disabling its module would restore the mod again.
                    original.Existed = false;
                    state.Originals[relative] = original;
                    continue;
                }
                var backupRelative = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relative.ToUpperInvariant()))) + ".bin";
                var backup = CryptoAndIO.SafeChildPath(_backupRoot, backupRelative);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                File.Copy(target, backup, true);
                original.BackupRelativePath = backupRelative;
                original.Sha256 = actualHash;
            }
            state.Originals[relative] = original;
        }

        if (settings is not null && (GameMod.IsVanilla(settings) || !settings.PawPatchEnabled || settings.DataOnly))
            await PreserveSteamOriginalsAsync(state, allPaths, desired, cancellationToken);
        if (preserveVanillaBootstrap) await PreserveVanillaBootstrapAsync(state, cancellationToken);
        var changes = new List<string>();
        var customPaths = previous.Modules.GetValueOrDefault(CustomRwdMods.ModuleId)?.Files.Select(f => CryptoAndIO.NormalizeRelativePath(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        customPaths.UnionWith(desired.GetValueOrDefault(CustomRwdMods.ModuleId)?.Files.Select(f => CryptoAndIO.NormalizeRelativePath(f.Path)) ?? []);
        foreach (var relative in allPaths)
        {
            var target = PatchRecovery.GamePath(_gameRoot, relative);
            if (customMods is not null && File.Exists(target) && MultiplayerCheck.IsHotkeyFile(relative)) continue;
            var expected = winners.TryGetValue(relative, out var winner) ? winner.File?.Sha256 : state.Originals.GetValueOrDefault(relative)?.Sha256;
            if (!File.Exists(target) ? expected is not null : expected is null || !(await CryptoAndIO.Sha256Async(target, cancellationToken)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                if (customMods is not null && !customPaths.Contains(relative)) throw new IOException("Repair the game files before applying custom mods: " + relative);
                changes.Add(relative);
            }
        }
        ensureGameClosed?.Invoke();
        var snapshot = await recovery.CaptureAsync(changes, previous, ct: cancellationToken);
        try
        {
            foreach (var relative in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ensureGameClosed?.Invoke();
                var target = PatchRecovery.GamePath(_gameRoot, relative);

                if (winners.TryGetValue(relative, out var winner))
                {
                    var suppliedFile = winner.File;
                    if (suppliedFile is not null)
                    {
                        var source = CryptoAndIO.SafeChildPath(Path.Combine(_packageRoot, Sanitize(winner.Id), Sanitize(winner.Module.Version), "payload"), relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        var temporary = target + ".pawpatch.tmp";
                        File.Copy(source, temporary, true);
                        File.Move(temporary, target, true);
                    }
                    else if (File.Exists(target)) File.Delete(target);
                }
                else if (state.Originals.TryGetValue(relative, out var original) && original.Existed && original.BackupRelativePath is not null)
                {
                    var source = CryptoAndIO.SafeChildPath(_backupRoot, original.BackupRelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, true);
                }
                else if (File.Exists(target)) File.Delete(target);
            }

            if (resetOriginals)
            {
                // An empty module list would make VerifyAsync vacuously succeed.
                // Verify restoration inside the transaction so failure rolls back.
                foreach (var relative in allPaths)
                {
                    var original = state.Originals[relative];
                    var target = PatchRecovery.GamePath(_gameRoot, relative);
                    if (original.Existed
                        ? !File.Exists(target) || !(await CryptoAndIO.Sha256Async(target)).Equals(original.Sha256, StringComparison.OrdinalIgnoreCase)
                        : File.Exists(target)) throw new IOException("Uninstall verification failed: " + relative);
                }
            }
            state.Modules = desired.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            state.LastSuccessfulUpdate = DateTimeOffset.UtcNow.ToString("O");
            state.AppliedSettings = settings is null ? null : JsonSerializer.Deserialize(JsonSerializer.Serialize(settings, LauncherJsonContext.Default.UserSettings), LauncherJsonContext.Default.UserSettings);
            state.ReleaseId = releaseId;
            state.GameRequirement = gameRequirement;
            state.BaseGameSha256 = baseGameSha256;
            if (resetOriginals) state.Originals.Clear(); // A later fresh install must capture its new baseline.
            await CryptoAndIO.AtomicWriteTextAsync(_statePath, JsonSerializer.Serialize(state, LauncherJsonContext.Default.InstallState), cancellationToken);
            var errors = await VerifyAsync(cancellationToken, allowCustomHotkeys: customMods is not null);
            if (errors.Count > 0) throw new IOException("Installation verification failed: " + string.Join("; ", errors.Take(5)));
            await recovery.CommitAsync(snapshot.Directory, snapshot.Journal);
        }
        catch
        {
            if (snapshot.Journal.Phase == "prepared")
            {
                await recovery.RestoreAsync(snapshot.Directory, snapshot.Journal);
                snapshot.Journal.Phase = "recovered";
                await PatchRecovery.SaveAsync(snapshot.Directory, snapshot.Journal);
            }
            throw;
        }
    }

    public async Task UninstallAsync(CancellationToken cancellationToken = default, UserSettings? settings = null, string? releaseId = null)
    {
        RemovalSafety.CheckNoLinks(_controlRoot);
        await new PatchRecovery(_gameRoot).RecoverInterruptedAsync();
        var state = LoadState();
        if (state.Modules.Count == 0)
        {
            if (settings is not null) await ReconcileAsync(new Dictionary<string, InstalledModule>(), cancellationToken, settings, releaseId);
            return;
        }
        var expected = MultiplayerCheck.Expected(state);
        var paths = state.Modules.Values.SelectMany(m => m.Files.Select(f => f.Path).Concat(m.Remove))
            .Select(CryptoAndIO.NormalizeRelativePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Validate every original and every live conflict BEFORE changing any game file.
        foreach (var relative in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = PatchRecovery.GamePath(_gameRoot, relative);
            RemovalSafety.CheckNoLinks(target);
            if (Directory.Exists(target)) throw new IOException("Managed file became a directory: " + relative);
            if (!state.Originals.TryGetValue(relative, out var original))
                throw new InvalidDataException("Original file record is missing: " + relative);
            if (original.Existed)
            {
                if (string.IsNullOrWhiteSpace(original.BackupRelativePath) || string.IsNullOrWhiteSpace(original.Sha256))
                    throw new IOException("Original backup record is incomplete: " + relative);
                var backup = CryptoAndIO.SafeChildPath(_backupRoot, original.BackupRelativePath);
                RemovalSafety.CheckNoLinks(backup);
                if (!File.Exists(backup) || !(await CryptoAndIO.Sha256Async(backup, cancellationToken)).Equals(original.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("Original backup is missing or damaged: " + relative);
            }
            else if (relative.Equals("k2.exe", StringComparison.OrdinalIgnoreCase))
                throw new IOException("Refusing to remove the original game executable.");
            if (!File.Exists(target)) continue;
            var actual = await CryptoAndIO.Sha256Async(target, cancellationToken);
            if (original.Existed && actual.Equals(original.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            if (!expected.TryGetValue(relative, out var installed) || installed is null || !actual.Equals(installed.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("File was changed outside the launcher; uninstall stopped to preserve it: " + relative);
        }
        await ReconcileAsync(new Dictionary<string, InstalledModule>(), cancellationToken, settings, releaseId, resetOriginals: true,
            preserveVanillaBootstrap: settings is not null && GameMod.IsVanilla(settings));
    }

    private async Task PreserveSteamOriginalsAsync(InstallState state, IReadOnlyCollection<string> paths,
        IReadOnlyDictionary<string, InstalledModule> desired, CancellationToken cancellationToken)
    {
        var missing = SteamVanillaBaseline.Files.Where(pair => paths.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)
            && state.Originals.TryGetValue(pair.Key, out var original) && !original.Existed).ToArray();
        if (missing.Length == 0 || !await SteamVanillaBaseline.MatchesGameAsync(_gameRoot, cancellationToken)) return;
        Directory.CreateDirectory(_backupRoot);
        foreach (var (relative, hash) in missing)
        {
            var backupName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relative.ToUpperInvariant()))) + ".bin";
            var backup = CryptoAndIO.SafeChildPath(_backupRoot, backupName);
            if (Path.GetExtension(relative).Equals(".rwd", StringComparison.OrdinalIgnoreCase))
            {
                // Stock skin archives are also shipped unchanged by Arcane Wars.
                // Recover them from the live file or a verified cached package.
                var candidates = new[] { PatchRecovery.GamePath(_gameRoot, relative) }.Concat(state.Modules.Concat(desired)
                    .Where(pair => Provides(pair.Value, relative) is { } file && file.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    .Select(pair => CryptoAndIO.SafeChildPath(Path.Combine(_packageRoot, Sanitize(pair.Key), Sanitize(pair.Value.Version), "payload"), relative)));
                string? source = null;
                foreach (var candidate in candidates)
                {
                    RemovalSafety.CheckNoLinks(candidate);
                    if (File.Exists(candidate) && (await CryptoAndIO.Sha256Async(candidate, cancellationToken)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    { source = candidate; break; }
                }
                if (source is null) throw new IOException("The original Steam archive is missing or damaged: " + relative);
                File.Copy(source, backup, true);
            }
            else await File.WriteAllBytesAsync(backup, SteamVanillaBaseline.Read(relative), cancellationToken);
            state.Originals[relative] = new OriginalFile { Existed = true, Sha256 = hash, BackupRelativePath = backupName };
        }
    }

    private async Task PreserveVanillaBootstrapAsync(InstallState state, CancellationToken cancellationToken)
    {
        // Legacy adoption treated the stock bootstrap, which is also distributed
        // by startup-base, as a mod file. Removing it prevents the engine from
        // mounting Data.rwd ("Work Depot not specified"). Keep a real original
        // when available; otherwise recover only this verified base-game file.
        var relative = CryptoAndIO.NormalizeRelativePath("startup/autoexec.txt");
        if (!state.Originals.TryGetValue(relative, out var original) || original.Existed
            || !state.Modules.TryGetValue("startup-base", out var startup)) return;
        var file = Provides(startup, relative) ?? throw new InvalidDataException("The base-game startup file is missing from its package.");
        var source = CryptoAndIO.SafeChildPath(Path.Combine(_packageRoot, "startup-base", Sanitize(startup.Version), "payload"), relative);
        RemovalSafety.CheckNoLinks(source);
        if (!File.Exists(source) || !(await CryptoAndIO.Sha256Async(source, cancellationToken)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The original startup package is missing or damaged. Repair the installation before switching to Vanilla.");
        var backupName = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relative.ToUpperInvariant()))) + ".bin";
        var backup = CryptoAndIO.SafeChildPath(_backupRoot, backupName);
        Directory.CreateDirectory(_backupRoot);
        File.Copy(source, backup, true);
        state.Originals[relative] = new OriginalFile { Existed = true, Sha256 = file.Sha256, BackupRelativePath = backupName };
    }

    public async Task<IReadOnlyList<string>> VerifyAsync(CancellationToken cancellationToken = default, bool allowCustomHotkeys = false)
    {
        var state = LoadState();
        var errors = new List<string>();
        foreach (var entry in MultiplayerCheck.Expected(state))
        {
            var relative = entry.Key;
            var expected = entry.Value;
            var target = PatchRecovery.GamePath(_gameRoot, relative);
            if (expected is null)
            {
                if (File.Exists(target)) errors.Add($"Should be removed: {relative}");
                continue;
            }
            if (!File.Exists(target)) { errors.Add($"Missing: {relative}"); continue; }
            var actual = await CryptoAndIO.Sha256Async(target, cancellationToken);
            // Installation/rollback validation stays byte-exact. Readiness and
            // launch may accept an existing, deliberately remapped input script.
            if (!actual.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase)
                && !(allowCustomHotkeys && MultiplayerCheck.IsHotkeyFile(relative))) errors.Add($"Changed: {relative}");
        }
        return errors;
    }

    private static ModuleFile? Provides(InstalledModule module, string relative)
        => module.Files.FirstOrDefault(file => CryptoAndIO.NormalizeRelativePath(file.Path).Equals(relative, StringComparison.OrdinalIgnoreCase));

    private static bool Removes(InstalledModule module, string relative)
        => module.Remove.Any(path => CryptoAndIO.NormalizeRelativePath(path).Equals(relative, StringComparison.OrdinalIgnoreCase));

    private static string Sanitize(string value)
    {
        if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..", StringComparison.Ordinal))
            throw new InvalidDataException($"Invalid package identifier: {value}");
        return value;
    }
}
