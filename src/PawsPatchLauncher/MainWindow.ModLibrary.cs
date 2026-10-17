namespace PawsPatchLauncher;

public partial class MainWindow
{
    private ChannelManifest? _offeredModChannel;
    private bool _selectedModStored;
    private ChannelManifest? _selectedInstalledRelease;
    private string? _modLibraryRepairKey;
    private bool _selectionRequiresUpdate;
    private bool _selectionUpdateAvailable;

    private bool CatalogSupportsSelection(ChannelManifest channel)
    {
        var active = EffectiveSettingsForGame(_settings, channel);
        try { GamePackageSelector.Select(channel, active, active.RussianLocalization, active.CustomPlayerColors); return true; }
        catch (InvalidDataException) { return false; }
    }

    private string SelectionUpdateText => _selectionUpdateAvailable
        ? T("Для выбранных языков или компонентов нужны новые файлы мода. Нажмите «Обновить и применить». Выбранные языки сохранятся.",
                "The selected languages or components need newer mod files. Use Update and apply. Your language choices will be kept.")
        : _settings.PinnedRelease is not null
            ? T("Выбранный старый выпуск не поддерживает эти языки или компоненты. Выберите последнюю версию патча либо настройки, доступные в этом выпуске.",
                "The pinned release does not support these languages or components. Select the latest patch release or settings supported by this release.")
            : T("Сохранённый выпуск не поддерживает эти языки или компоненты. Проверьте обновления при подключении к интернету либо выберите настройки сохранённого выпуска.",
                "The stored release does not support these languages or components. Check for updates when online, or choose settings supported by the stored release.");

    private bool LocallyAvailable(ModuleInstaller installer, PackageRelease package)
        => installer.IsPrepared(package) || _feedClient.IsPackageCached(package);

    private async Task AdoptLocalModsAsync(ChannelManifest? preferred = null)
    {
        if (_game is null) return;
        var library = new ModLibrary(_game.Directory);
        var installer = new ModuleInstaller(_game.Directory);
        var state = installer.LoadState();
        var candidates = new List<ChannelManifest>();
        // Keep the actually installed release when migrating an existing installation.
        if (state.ReleaseId is { Length: 64 } release)
            try { candidates.Add(_feedClient.LoadArchived(release, state.AppliedSettings?.Channel ?? _settings.Channel)); } catch { }
        if (preferred is not null) candidates.Add(preferred);
        candidates.AddRange(_feedClient.Archived("stable"));
        candidates.AddRange(_feedClient.Archived("beta"));
        foreach (var channel in candidates.DistinctBy(ChannelFingerprint.Create))
            foreach (var mod in new[] { GameMod.ArcaneWars, GameMod.Immortals, GameMod.Vanilla })
            {
                if (library.Find(mod, channel.Channel) is not null) continue;
                List<PackageRelease> packages;
                try { packages = ModLibrary.Packages(channel, mod); }
                catch (InvalidDataException) { continue; }
                if (packages.Count > 0 && packages.All(p => LocallyAvailable(installer, p))) await library.RememberAsync(channel, mod);
            }
    }

    private void SelectLibraryChannel()
    {
        _selectedModStored = false;
        _selectedInstalledRelease = null;
        _selectionRequiresUpdate = _selectionUpdateAvailable = false;
        if (_game is null) return;
        if (_modLibraryRepairKey == _settings.Mod + ":" + _settings.Channel)
        {
            _channel = _offeredModChannel ?? _channel;
            return;
        }
        var entry = new ModLibrary(_game.Directory).Find(_settings.Mod, _settings.Channel);
        if (entry is not null && (_settings.PinnedRelease is null || entry.ReleaseId == _settings.PinnedRelease))
        {
            var installed = _feedClient.LoadArchived(entry.ReleaseId, entry.Channel);
            if (entry.ContentId != ModLibrary.ContentId(installed, entry.Mod) && entry.ContentId != ModLibrary.LegacyContentId(installed, entry.Mod))
                throw new InvalidDataException("Installed mod metadata does not match its signed release.");
            var installer = new ModuleInstaller(_game.Directory);
            var storedPackages = ModLibrary.Packages(installed, _settings.Mod);
            _selectedModStored = storedPackages.Count > 0 && storedPackages.All(p => LocallyAvailable(installer, p));
            if (_selectedModStored)
            {
                _selectedInstalledRelease = installed;
                // Text/speech are downloaded separately. A retained mod must not
                // freeze their catalog at the release that first installed it.
                // Explicit pins and changed gameplay packages keep their own release.
                _channel = GameLanguages.SelectionCatalog(installed, _offeredModChannel, _settings);
                // An older offered language catalog must not break a usable retained release.
                if (!ReferenceEquals(_channel, installed) && !CatalogSupportsSelection(_channel) && CatalogSupportsSelection(installed))
                    _channel = installed;
                if (!CatalogSupportsSelection(_channel))
                {
                    _selectionRequiresUpdate = true;
                    _selectionUpdateAvailable = _settings.PinnedRelease is null && _offeredModChannel is { } offered
                        && offered.Channel == installed.Channel && CatalogSupportsSelection(offered);
                    // Preview only: the installed release and game files stay unchanged until
                    // the explicit Update and apply action prepares the complete new release.
                    if (_selectionUpdateAvailable) _channel = _offeredModChannel;
                }
                return;
            }
        }
        _channel = _offeredModChannel ?? _channel;
        if (_channel is not null)
        {
            var installer = new ModuleInstaller(_game.Directory);
            try { _selectedModStored = ModLibrary.Packages(_channel, _settings.Mod).All(p => LocallyAvailable(installer, p)); }
            catch (InvalidDataException) { }
        }
        // The original English game is usable even before any mod has been downloaded.
        if (GameMod.IsVanilla(_settings) && GameLanguages.Text(_settings) == "en" && (_channel is null
            || !_channel.Packages.Any(p => p.Id == "game-localization-en")))
            _selectedModStored = true;
    }

    private async Task<InstalledModule> PrepareStoredComponentAsync(ModuleInstaller installer, PackageRelease package)
    {
        try
        {
            if (installer.IsPrepared(package)) return await installer.ReadPreparedAsync(package);
            // Languages are fetched on Apply, independently of the retained mod library.
            if (GameLanguages.IsLanguage(package))
            {
                var languageArchive = await DownloadPackageAsync(package);
                return await installer.PrepareAsync(package, languageArchive);
            }
            var archive = await _feedClient.GetCachedPackageAsync(package);
            return await installer.PrepareAsync(package, archive);
        }
        catch (Exception error) when (error is IOException or InvalidDataException)
        {
            _modLibraryRepairKey = _settings.Mod + ":" + _settings.Channel;
            throw new IOException(T("Сохранённые файлы компонента повреждены или отсутствуют. Нажмите «Установить», чтобы восстановить мод: ",
                "Stored component files are missing or damaged. Use Install to restore the mod: ") + package.Name.Get(_text.Language), error);
        }
    }

    private async Task<Dictionary<string, InstalledModule>> InstallModLibraryAsync(ChannelManifest channel, string mod)
    {
        EnsureModDownloadAccess(mod);
        if (_game is null) throw new InvalidOperationException(_text["status.notfound"]);
        var packages = ModLibrary.Packages(channel, mod);
        var installer = new ModuleInstaller(_game.Directory);
        var missing = packages.Where(p => !LocallyAvailable(installer, p)).ToList();
        ShowWorking(() => T("Подготавливаю мод и все его компоненты…", "Preparing the mod and all its components…"));
        TransferText.Text = T("Всего к загрузке: ", "Total download: ") + FormatBytes(missing.Sum(p => p.Size));
        TransferText.Visibility = System.Windows.Visibility.Visible;
        var prepared = new Dictionary<string, InstalledModule>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in packages)
        {
            EnsureModDownloadAccess(mod);
            if (installer.IsPrepared(package))
            {
                try { prepared[package.Id] = await installer.ReadPreparedAsync(package); continue; }
                catch (Exception error) when (error is IOException or InvalidDataException) { ActivityStore.Log(error); }
            }
            var archive = await DownloadPackageAsync(package);
            prepared[package.Id] = await installer.PrepareAsync(package, archive, force: true);
        }
        // Only a fully prepared release is registered. Other mods remain in the library.
        await new ModLibrary(_game.Directory).RememberAsync(channel, mod);
        _modLibraryRepairKey = null;
        return prepared;
    }
}
