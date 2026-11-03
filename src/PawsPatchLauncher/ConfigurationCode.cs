namespace PawsPatchLauncher;

public static class ConfigurationCode
{
    public static UserSettings Parse(string code)
    {
        var voiceCode = code.Trim().ToUpperInvariant();
        if (GameLanguages.VoiceChoices.Any(language => voiceCode.EndsWith("-VO" + language.ToUpperInvariant())))
        {
            var settings = Parse(voiceCode[..^5]);
            settings.GameVoiceLanguage = voiceCode[^2..].ToLowerInvariant();
            return settings;
        }
        if (GameLanguages.Choices.Where(c => c is not ("en" or "ru")).Any(c => voiceCode.EndsWith("-TX" + c.ToUpperInvariant())))
        {
            var settings = Parse(voiceCode[..^5]);
            GameLanguages.SetText(settings, voiceCode[^2..].ToLowerInvariant());
            return settings;
        }
        var parts = code.Trim().ToUpperInvariant().Split('-');
        if (parts.Length >= 3 && parts[0] == "PAW" && parts[1] is "BETA" or "STABLE" && parts[2] is "VANILLA" or "IMMORTALS")
        {
            var mod = new UserSettings { Mod = parts[2] == "VANILLA" ? GameMod.Vanilla : GameMod.Immortals,
                Channel = parts[1].ToLowerInvariant(), RussianLocalization = false };
            var index = 3;
            if (index < parts.Length && parts[index] is "RU0" or "RU1") mod.RussianLocalization = parts[index++] == "RU1";
            if (index < parts.Length && parts[index] == "PP1") { GameMod.SetPawPatch(mod, true); index++; }
            if (index < parts.Length && parts[index] == "CL1") { GameMod.SetColors(mod, true); index++; }
            if (index < parts.Length && parts[index] == "OOS1") { GameMod.SetDesync(mod, true); index++; }
            if (index < parts.Length && parts[index] == "PB1")
            {
                if (!GameMod.PawPatchSelected(mod) || parts.Length < index + 6)
                    throw new FormatException("Pure options require the enabled patch.");
                mod.PureBetaFeatures = true; index++;
                bool Option(string prefix)
                {
                    var value = parts[index++];
                    return value == prefix + "1" ? true : value == prefix + "0" ? false
                        : throw new FormatException("Invalid Pure beta option: " + prefix);
                }
                var pure = GameMod.PureComponents(mod);
                pure.ImprovedAi = Option("AI"); pure.WoundedLairDefenders = Option("LR");
                pure.IndependentHostility = Option("IW");
                pure.RoamingSpawnMode = parts[index++] switch { "SP1" => "standard", "SP2" => "x2", "SP4" => "x4", _ => throw new FormatException("Invalid Pure frequency.") };
                pure.AdditionalRoamingCompanies = Option("RM");
            }
            if (index < parts.Length && parts[index] == "DATA" && (GameMod.PawPatchSelected(mod) || mod.Mod == GameMod.Immortals)) { mod.DataOnly = true; index++; }
            if (index != parts.Length) throw new FormatException("Invalid mod configuration field.");
            if (mod.PureBetaFeatures && mod.DataOnly) throw new FormatException("Pure beta options require a supported game executable.");
            if ((GameMod.ColorsSelected(mod) || GameMod.DesyncSelected(mod)) && (!GameMod.PawPatchSelected(mod) || mod.DataOnly))
                throw new FormatException("Executable options require the enabled patch.");
            return EffectiveSettings.ForChannel(mod);
        }
        var core = parts.LastOrDefault() != "PP0";
        var dataOnly = parts.LastOrDefault() == "DATA";
        if (dataOnly) parts = parts[..^1];
        core = parts.LastOrDefault() != "PP0";
        if (!core) parts = parts[..^1];
        var exhaustion = parts.LastOrDefault() == "ZM1";
        if (exhaustion) parts = parts[..^1];
        var improvedAi = parts.LastOrDefault() == "AI1";
        if (improvedAi) parts = parts[..^1];
        if (parts.Length is not (10 or 11) || parts[0] != "PAW" || parts[1] is not ("BETA" or "STABLE"))
            throw new FormatException("PAW-STABLE-IW1-SP4-RM1-SG1-LM1-RU1-CL0-OOS0");
        bool Flag(int index, string prefix)
        {
            if (parts[index] == prefix + "0") return false;
            if (parts[index] == prefix + "1") return true;
            throw new FormatException("Invalid configuration field: " + prefix);
        }
        var result = new UserSettings
        {
            PawPatchEnabled = core,
            IgnoreZeroMorale = exhaustion,
            ImprovedAi = improvedAi,
            DataOnly = dataOnly,
            Channel = parts[1].ToLowerInvariant(), IndependentHostility = Flag(2, "IW"),
            AdditionalRoamingCompanies = Flag(4, "RM"), SiegeBalance = Flag(5, "SG"),
            LargeMapSizes = Flag(6, "LM"), RussianLocalization = Flag(7, "RU"),
            CustomPlayerColors = Flag(8, "CL"), DesyncMode = Flag(9, "OOS") ? "continue" : "official",
            DisablePowersAndShards = parts.Length == 10 || Flag(10, "PS"),
            RoamingSpawnMode = parts[3] switch { "SP4" => "x4", "SP2" => "x2", "SP1" => "standard", _ => throw new FormatException("Invalid SP field") }
        };
        if (exhaustion && (!core || dataOnly)) throw new FormatException("Exhaustion requires the enabled Arcane Wars patch.");
        if (improvedAi && (!core || dataOnly))
            throw new FormatException("Improved AI requires the enabled Arcane Wars patch.");
        if (result.LargeMapSizes != core)
            throw new FormatException("This combination is not supported by this launcher.");
        if (dataOnly && (result.CustomPlayerColors || result.IndependentHostility || result.DesyncMode != "official"))
            throw new FormatException("Executable features are not available in file-only mode.");
        return EffectiveSettings.ForChannel(result);
    }

    public static void Apply(UserSettings source, UserSettings target)
    {
        GameMod.Validate(source);
        ModChannelSelection.Remember(target);
        if (target.Mod != source.Mod || target.Channel != source.Channel) target.PinnedRelease = null;
        target.Channel = source.Channel;
        target.Mod = source.Mod;
        ModChannelSelection.Remember(target);
        GameLanguages.SetText(target, GameLanguages.Text(source));
        target.GameVoiceLanguage = GameLanguages.Voice(source);
        // Importing another mod keeps remembered Arcane Wars components.
        if (!GameMod.IsArcaneWars(source))
        {
            GameMod.SetPawPatch(target, GameMod.PawPatchSelected(source));
            GameMod.SetColors(target, GameMod.ColorsSelected(source));
            GameMod.SetDesync(target, GameMod.DesyncSelected(source));
            target.DataOnly = source.DataOnly;
            target.PureBetaFeatures = source.PureBetaFeatures;
            if (source.PureBetaFeatures)
            {
                var from = GameMod.PureComponents(source); var to = GameMod.PureComponents(target);
                to.ImprovedAi = from.ImprovedAi; to.WoundedLairDefenders = from.WoundedLairDefenders;
                to.IndependentHostility = from.IndependentHostility;
                to.AdditionalRoamingCompanies = from.AdditionalRoamingCompanies; to.RoamingSpawnMode = from.RoamingSpawnMode;
            }
            return;
        }
        GameMod.SetPawPatch(target, source.PawPatchEnabled);
        target.CustomPlayerColors = source.CustomPlayerColors;
        target.DesyncMode = source.DesyncMode;
        target.IndependentHostility = source.IndependentHostility;
        target.RoamingSpawnMode = source.RoamingSpawnMode;
        target.AdditionalRoamingCompanies = source.AdditionalRoamingCompanies;
        target.SiegeBalance = source.SiegeBalance;
        target.IgnoreZeroMorale = source.IgnoreZeroMorale;
        target.ImprovedAi = source.ImprovedAi;
        target.DisablePowersAndShards = source.DisablePowersAndShards;
        target.LargeMapSizes = source.PawPatchEnabled;
        if (!target.PawPatchEnabled) GameMod.DisableArcaneComponents(target);
    }

    public static string Create(UserSettings settings)
    {
        var code = CreateComponents(settings);
        var text = GameLanguages.Text(settings);
        if (text is not ("en" or "ru")) code += "-TX" + text.ToUpperInvariant();
        return GameLanguages.Voice(settings) == text
            ? code : code + "-VO" + GameLanguages.Voice(settings).ToUpperInvariant();
    }

    private static string CreateComponents(UserSettings settings)
    {
        settings = EffectiveSettings.ForChannel(settings);
        var channel = settings.Channel.Equals("beta", StringComparison.OrdinalIgnoreCase) ? "BETA" : "STABLE";
        if (!GameMod.IsArcaneWars(settings))
        {
            var pure = GameMod.PureComponents(settings);
            var extras = settings.PureBetaFeatures ? $"-PB1-AI{Bit(pure.ImprovedAi)}-LR{Bit(pure.WoundedLairDefenders)}-IW{Bit(pure.IndependentHostility)}-SP{(pure.RoamingSpawnMode == "x4" ? 4 : pure.RoamingSpawnMode == "x2" ? 2 : 1)}-RM{Bit(pure.AdditionalRoamingCompanies)}" : "";
            return $"PAW-{channel}-{(GameMod.IsVanilla(settings) ? "VANILLA" : "IMMORTALS")}{(settings.RussianLocalization ? "-RU1" : "")}{(settings.PawPatchEnabled ? "-PP1" : "")}{(settings.CustomPlayerColors ? "-CL1" : "")}{(settings.DesyncMode == "continue" ? "-OOS1" : "")}{extras}{(settings.DataOnly ? "-DATA" : "")}";
        }
        var spawn = settings.RoamingSpawnMode.ToLowerInvariant() switch { "x4" => "4", "x2" => "2", _ => "1" };
        var oos = settings.DesyncMode.Equals("continue", StringComparison.OrdinalIgnoreCase) ? "1" : "0";
        // Legacy codes already mean powers/shards disabled; preserve their fingerprints.
        var powers = settings.DisablePowersAndShards ? "" : "-PS0";
        return $"PAW-{channel}-IW{Bit(settings.IndependentHostility)}-SP{spawn}-RM{Bit(settings.AdditionalRoamingCompanies)}-SG{Bit(settings.SiegeBalance)}-LM{Bit(settings.PawPatchEnabled)}-RU{Bit(settings.RussianLocalization)}-CL{Bit(settings.CustomPlayerColors)}-OOS{oos}{powers}{(settings.ImprovedAi ? "-AI1" : "")}{(settings.IgnoreZeroMorale ? "-ZM1" : "")}{(settings.PawPatchEnabled ? "" : "-PP0")}{(settings.DataOnly ? "-DATA" : "")}";
    }

    private static int Bit(bool value) => value ? 1 : 0;
}
