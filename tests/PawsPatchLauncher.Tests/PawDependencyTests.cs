using PawsPatchLauncher;
using System.Text.Json;

internal static class PawDependencyTests
{
    internal static int Run()
    {
        var n=0;void Check(bool ok,string why){if(!ok)throw new Exception("Paw dependency: "+why);n++;}
        var feed=new ChannelManifest { Packages=[new(){Id="arcane-wars"},new(){Id="startup-base"},new(){Id="aw-localization-ru"},
            new(){Id="menu-runtime"},new(){Id="player-colors"},new(){Id="pawpatch-core",Required=true}] };
        static bool Disabled(UserSettings s)=>!s.CustomPlayerColors&&!s.IndependentHostility&&!s.AdditionalRoamingCompanies
            &&!s.SiegeBalance&&!s.DisablePowersAndShards&&!s.LargeMapSizes&&s.DesyncMode=="official"&&s.RoamingSpawnMode=="standard";
        foreach(var russian in new[]{false,true})
        foreach(var spawn in new[]{"standard","x2","x4"})
        for(var bits=0;bits<64;bits++)
        {
            var preferences=new UserSettings { PawPatchEnabled=false,RussianLocalization=russian,RoamingSpawnMode=spawn,
                CustomPlayerColors=(bits&1)!=0,IndependentHostility=(bits&2)!=0,AdditionalRoamingCompanies=(bits&4)!=0,
                SiegeBalance=(bits&8)!=0,DisablePowersAndShards=(bits&16)!=0,DesyncMode=(bits&32)!=0?"continue":"official" };
            var active=EffectiveSettings.ForFeed(preferences,feed);
            Check(Disabled(active)&&active.RussianLocalization==russian,"Active projection retained options or changed text language");
            var selected=GamePackageSelector.Select(feed,preferences,russian,preferences.CustomPlayerColors);
            var expected=new[]{"arcane-wars","startup-base","menu-runtime"}.Concat(russian?new[]{"aw-localization-ru"}:[]);
            Check(selected.Select(p=>p.Id).Order().SequenceEqual(expected.Order()),"Raw legacy preferences selected standalone patch packages");
            Check(GameExecutableSelector.Select(new(),preferences,feed)=="k2_paws_menu_1372.exe","Launch chose a component executable without its core");
            preferences.PawPatchEnabled = true;
            var original = ConfigurationCode.Create(preferences);
            GameMod.SetPawPatch(preferences, false);
            Check(Disabled(preferences) && preferences.SuspendedArcaneComponents is not null, "Off failed to suspend choices");
            var applied = EffectiveSettings.ForChannel(preferences);
            Check(applied.SuspendedArcaneComponents is null && Disabled(applied), "Applied state contains suspended preferences");
            // Persist exactly the settings document used by the launcher, including a mod switch while off.
            ModChannelSelection.SelectMod(preferences, GameMod.Immortals);
            preferences = JsonSerializer.Deserialize(JsonSerializer.Serialize(preferences, LauncherJsonContext.Default.UserSettings), LauncherJsonContext.Default.UserSettings)!;
            GameMod.NormalizeRememberedComponents(preferences);
            ModChannelSelection.SelectMod(preferences, GameMod.ArcaneWars);
            GameMod.SetPawPatch(preferences, false); // repeated save/off must not overwrite the remembered selection
            GameMod.SetPawPatch(preferences, true);
            Check(ConfigurationCode.Create(preferences) == original && preferences.SuspendedArcaneComponents is null, "Restart/mod switch did not restore exact choices");
            preferences.SiegeBalance = !preferences.SiegeBalance;
            var updated = ConfigurationCode.Create(preferences);
            GameMod.SetPawPatch(preferences, false); GameMod.SetPawPatch(preferences, true);
            Check(ConfigurationCode.Create(preferences) == updated, "Second suspension restored older choices");
        }
        var oldCode=ConfigurationCode.Parse("PAW-BETA-IW1-SP4-RM1-SG1-LM0-RU1-CL1-OOS1-PP0-VOEN");
        Check(Disabled(oldCode)&&oldCode.RussianLocalization&&GameLanguages.Voice(oldCode)=="en","Legacy import bypassed dependency or changed speech");
        var target=new UserSettings();ConfigurationCode.Apply(oldCode,target);Check(Disabled(target)&&!target.PawPatchEnabled,"Applying a copied configuration bypassed dependency");
        var changed=new UserSettings{CustomPlayerColors=true,DesyncMode="continue",RussianLocalization=true,GameVoiceLanguage="en"};
        GameMod.SetPawPatch(changed,false);Check(Disabled(changed)&&changed.RussianLocalization&&GameLanguages.Voice(changed)=="en","Master switch changed languages or kept components");
        GameMod.SetPawPatch(changed,true);Check(changed.CustomPlayerColors&&changed.SiegeBalance&&changed.RoamingSpawnMode=="x4"&&changed.DesyncMode=="continue","Re-enabling the core lost previous choices");
        var imported = new UserSettings { CustomPlayerColors=true, RoamingSpawnMode="x2" };
        var previousCode = ConfigurationCode.Create(imported);
        ConfigurationCode.Apply(oldCode, imported); GameMod.SetPawPatch(imported,true);
        imported.Channel="stable";imported.GameVoiceLanguage=null;
        Check(ConfigurationCode.Create(imported)==previousCode,"Importing a disabled core lost local suspended components");
        var legacy = new UserSettings { PawPatchEnabled=false,CustomPlayerColors=true,DesyncMode="continue" };
        GameMod.NormalizeRememberedComponents(legacy);GameMod.SetPawPatch(legacy,true);
        Check(legacy.CustomPlayerColors&&legacy.DesyncMode=="continue","Migration lost old suspended choices");
        var disabledImport=ConfigurationCode.Parse("PAW-STABLE-IW0-SP1-RM0-SG0-LM0-RU1-CL0-OOS0-PS0-PP0");
        Check(disabledImport.SuspendedArcaneComponents is null,"Configuration import contains local-only preference metadata");
        var before=new InstallState{AppliedSettings=new UserSettings{PawPatchEnabled=false,IndependentHostility=true,DesyncMode="continue"}};
        Check(UpdateDetector.HasSettingsChanges(before,[],EffectiveSettings.ForChannel(before.AppliedSettings)),"Legacy applied executable settings were marked ready without applying");
        Console.WriteLine($"PAW DEPENDENCY PASS {n}: all 384 stale option profiles, package and runtime selection, languages, legacy import, master toggle and unapplied migration.");
        return n;
    }
}
