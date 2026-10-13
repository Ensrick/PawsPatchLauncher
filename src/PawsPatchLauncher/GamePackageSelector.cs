namespace PawsPatchLauncher;

public static class GamePackageSelector
{
    // Shared by the UI and clean-install regression tests; preserves UI selection semantics.
    public static List<PackageRelease> Select(ChannelManifest channel, UserSettings settings, bool russianLocalization, bool customPlayerColors)
    {
        if (GameMod.IsArcaneWars(settings) && !settings.PawPatchEnabled)
        {
            settings = EffectiveSettings.ForChannel(settings);
            customPlayerColors = false;
        }
        var text = settings.GameTextLanguage ?? (russianLocalization ? "ru" : "en");
        var packages = SelectComponents(channel, settings, text == "ru", customPlayerColors);
        PackageRelease LanguagePackage(string id) => channel.Packages.SingleOrDefault(p => p.Id == id)
            ?? throw new InvalidDataException("В этом выпуске нет файлов выбранного языка. Выберите новый выпуск. / This release does not include the selected language. Select a newer release.");
        if (text is "de" or "fr" or "cs" or "uk")
        {
            packages.Add(LanguagePackage("game-localization-" + text));
            var modText = settings.Mod == GameMod.Immortals ? "immortals-localization-" + text
                : GameMod.IsArcaneWars(settings) ? (settings.PawPatchEnabled && !settings.DataOnly ? "localization-" : "aw-localization-") + text : null;
            // Early 0.8.1 preview feeds only provided the original game translation.
            // Retain support for those saved releases, adding mod text when offered.
            if (modText is not null && (text is "cs" or "uk" || channel.Packages.Any(p => p.Id == modText)))
            {
                packages.Add(LanguagePackage(modText));
                if (GameMod.IsArcaneWars(settings) && settings.DataOnly && settings.PawPatchEnabled)
                {
                    packages.RemoveAll(p => p.Id == "pawpatch-data");
                    packages.Add(LanguagePackage("pawpatch-data-" + text));
                }
            }
        }
        var voice = settings.GameVoiceLanguage ?? GameLanguages.DefaultVoice(text);
        if (GameLanguages.SupportsSeparateVoice(channel))
        {
            if (voice != "en") packages.Add(LanguagePackage("game-voice-" + voice));
        }
        else if (voice != text || text is "de" or "fr" or "cs" or "uk")
            throw new InvalidDataException("Этот старый выпуск не поддерживает отдельный выбор озвучки. Выберите новый выпуск. / This older release does not support separate speech. Select a newer release.");
        return packages;
    }

    private static List<PackageRelease> SelectComponents(ChannelManifest channel, UserSettings settings, bool russianLocalization, bool customPlayerColors)
    {
        GameMod.Validate(settings);
        if (settings.DataOnly)
        {
            settings = EffectiveSettings.ForChannel(settings);
            customPlayerColors = false;
        }
        if (!GameMod.IsArcaneWars(settings))
        {
            var baseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (settings.Mod == GameMod.Immortals) { baseIds.Add("immortals"); baseIds.Add("startup-base"); }
            if (!settings.DataOnly && GameExecutableSelector.HasMenuRuntime(channel)
                && (settings.Mod == GameMod.Immortals || GameMod.PawPatchSelected(settings))) baseIds.Add("menu-runtime");
            if (russianLocalization) baseIds.Add(settings.Mod == GameMod.Immortals ? "immortals-localization-ru" : "vanilla-localization-ru");
            else if (channel.Packages.Any(p => p.Id == "game-localization-en")) baseIds.Add("game-localization-en");
            if (GameMod.PawPatchSelected(settings) && GameMod.HasPureFixes(channel))
            {
                baseIds.Add("pure-fixes-data");
                if (!settings.DataOnly) baseIds.Add("pure-fixes-runtime");
                if (!settings.DataOnly && GameMod.HasPureBeta(channel))
                {
                    baseIds.Add("pure-beta-common");
                    baseIds.Add("pure-" + settings.Mod + "-rules");
                    baseIds.Add("pure-localization-bot-ui-" + GameLanguages.Text(settings));
                    if (settings.ImprovedAi) baseIds.Add("pure-ai-improvements");
                    if (!channel.PureLairRecoveryBuiltIn && GameMod.PureComponents(settings).WoundedLairDefenders) baseIds.Add("pure-lair-recovery");
                    if (settings.IndependentHostility) { baseIds.Add("pure-independent-hostility"); baseIds.Add("pure-" + settings.Mod + "-hostility"); }
                    baseIds.Add("pure-" + settings.Mod + "-roaming-" + settings.RoamingSpawnMode
                        + (settings.AdditionalRoamingCompanies ? "-with-new" : "-no-new"));
                }
                if (!settings.DataOnly && customPlayerColors)
                {
                    if (!GameMod.HasPureOptions(channel)) throw new InvalidDataException("This release does not support extended player colors.");
                    baseIds.Add("pure-player-colors");
                }
                if (settings.Mod == GameMod.Immortals && channel.Packages.Any(p => p.Id == "immortals-text-fixes"))
                    baseIds.Add("immortals-text-fixes");
            }
            var packages = channel.Packages.Where(p => baseIds.Contains(p.Id)).ToList();
            if (packages.Count != baseIds.Count || packages.Any(p => p.DependsOn.Any(d => !baseIds.Contains(d)))
                || settings.DataOnly && packages.Any(p => !p.ExecutableIndependent))
                throw new InvalidDataException("В этом выпуске нет файлов выбранного мода или языка. Проверьте обновления. / This release does not include the selected mod or language. Check for updates.");
            return packages;
        }
        if (settings.DataOnly || !settings.PawPatchEnabled) return SelectWithoutCore(channel, settings, russianLocalization, customPlayerColors);
        var ids = new HashSet<string>(channel.Packages.Where(x => x.Required && ModLibrary.BelongsTo(x, GameMod.ArcaneWars)).Select(x => x.Id), StringComparer.OrdinalIgnoreCase);
        if (!russianLocalization && channel.Packages.Any(p => p.Id == "game-localization-en")) ids.Add("game-localization-en");
        if (russianLocalization) ids.Add("localization-ru");
        if (customPlayerColors) ids.Add("player-colors");
        if (settings.DesyncMode == "continue") ids.Add("desync-continue");
        var fastSpawn = settings.RoamingSpawnMode.Equals("x4", StringComparison.OrdinalIgnoreCase);
        if (settings.RoamingSpawnMode.Equals("x2", StringComparison.OrdinalIgnoreCase))
            ids.Add(settings.AdditionalRoamingCompanies ? "roaming-profile-x2-with-new" : "roaming-profile-x2-no-new");
        else
        {
            if (!fastSpawn && settings.AdditionalRoamingCompanies) ids.Add("roaming-profile-standard-with-new");
            if (fastSpawn && !settings.AdditionalRoamingCompanies) ids.Add("roaming-profile-x4-no-new");
            if (!fastSpawn && !settings.AdditionalRoamingCompanies) ids.Add("roaming-profile-standard-no-new");
        }
        var botUi = "localization-bot-ui-" + GameLanguages.Text(settings);
        if (channel.Packages.Any(p => p.Id == botUi)) ids.Add(botUi);
        if (settings.ImprovedAi && GameMod.HasImprovedAi(channel))
            ids.Add("ai-improvements");
        if (!settings.SiegeBalance) ids.Add("siege-balance-standard");
        if (!settings.DisablePowersAndShards) ids.Add("powers-shards-original");
        bool changed;
        do
        {
            changed = false;
            foreach (var package in channel.Packages.Where(x => ids.Contains(x.Id)))
                foreach (var dependency in package.DependsOn)
                    if (ids.Add(dependency)) changed = true;
        } while (changed);
        var missing = ids.Where(id => channel.Packages.All(x => !x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count > 0) throw new InvalidDataException("Missing update packages: " + string.Join(", ", missing));
        return channel.Packages.Where(x => ids.Contains(x.Id)).ToList();
    }

    private static List<PackageRelease> SelectWithoutCore(ChannelManifest channel, UserSettings settings, bool russian, bool colors)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "arcane-wars", "startup-base" };
        if (!settings.DataOnly && GameExecutableSelector.HasMenuRuntime(channel)) ids.Add("menu-runtime");
        if (!russian && channel.Packages.Any(p => p.Id == "game-localization-en")) ids.Add("game-localization-en");
        if (settings.DataOnly && settings.PawPatchEnabled) ids.Add(russian ? "pawpatch-data-ru" : "pawpatch-data");
        if (russian)
        {
            ids.Add("aw-localization-ru");
            if (channel.Packages.Any(p => p.Id == "aw-localization-ru" && p.DependsOn.Contains("vanilla-localization-ru")))
                ids.Add("vanilla-localization-ru");
        }
        if (colors) ids.Add("aw-player-colors");
        if (settings.IndependentHostility || settings.DesyncMode == "continue" || colors) ids.Add("aw-runtime");
        if (settings.IndependentHostility) ids.Add("aw-hostility");
        if (settings.RoamingSpawnMode != "standard" || settings.AdditionalRoamingCompanies)
            ids.Add($"aw-roaming-{settings.RoamingSpawnMode}-{(settings.AdditionalRoamingCompanies ? "new" : "original")}");
        if (settings.SiegeBalance) ids.Add("aw-siege-balance");
        if (settings.DisablePowersAndShards) ids.Add("aw-powers-disabled");
        var missing = ids.Where(id => !channel.Packages.Any(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException("В этом выпуске нет отдельных компонентов для Arcane Wars без Paw's Patch. Выберите новый выпуск. / This release does not include standalone Arcane Wars components. Select a newer release.");
        // These packages are additive over Arcane Wars. Never expand legacy dependencies back into Paw's Patch.
        var result = channel.Packages.Where(p => ids.Contains(p.Id)).ToList();
        if (result.Any(p => p.DependsOn.Any(d => !ids.Contains(d))))
            throw new InvalidDataException("Invalid standalone Arcane Wars dependency.");
        if (settings.DataOnly && result.Any(p => !p.ExecutableIndependent))
            throw new InvalidDataException("Для файлового режима нужен совместимый выпуск пакетов. / This release does not support file-only mode.");
        return result;
    }
}
