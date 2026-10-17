using PawsPatchLauncher;

internal static class PureBetaTests
{
    internal static ChannelManifest Feed(string channel = "beta")
    {
        var f = new ChannelManifest { Channel = channel, PureRuntimeOptions = true };
        void Add(string id, params string[] mods) => f.Packages.Add(new() { Id = id, Mods = mods.Length == 0 ? ["vanilla", "immortals"] : [..mods], ExecutableIndependent = id is "pure-fixes-data" or "immortals" or "startup-base" });
        foreach (var id in new[] { "pure-fixes-data", "pure-fixes-runtime", "pure-player-colors", "pure-beta-common", "pure-ai-improvements", "pure-lair-recovery", "pure-independent-hostility", "immortals", "startup-base" }) Add(id);
        Add("pure-localization-bot-ui-en");
        foreach (var mod in new[] { "vanilla", "immortals" })
        {
            Add("pure-"+mod+"-rules",mod); Add("pure-"+mod+"-hostility",mod);
            foreach (var frequency in new[] { "standard", "x2", "x4" })
            foreach (var extra in new[] { "with-new", "no-new" }) Add("pure-"+mod+"-roaming-"+frequency+"-"+extra,mod);
        }
        return f;
    }
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string why) { count++; if(!ok)throw new Exception("Pure beta: "+why); }
        var feed=Feed();
        foreach(var mod in new[]{"vanilla","immortals"})
        foreach(var frequency in new[]{"standard","x2","x4"})
        for(int flags=0;flags<64;flags++)
        {
            bool Flag(int i)=>(flags&(1<<i))!=0;
            var s=new UserSettings {Mod=mod,Channel="beta",RussianLocalization=false,GameVoiceLanguage="en"};
            GameMod.SetPawPatch(s,true); var p=GameMod.PureComponents(s);
            p.Colors=Flag(0);p.IgnoreDesync=Flag(1);p.ImprovedAi=Flag(2);p.WoundedLairDefenders=Flag(3);
            p.IndependentHostility=Flag(4);p.AdditionalRoamingCompanies=Flag(5);p.RoamingSpawnMode=frequency;
            var active=EffectiveSettings.ForFeed(s,feed); var code=ConfigurationCode.Create(active);
            Check(active.PureBetaFeatures,"new feed must activate beta composition");
            var target=new UserSettings {Mod="arcane-wars",ImprovedAi=false,IndependentHostility=false,RoamingSpawnMode="x4"};
            ConfigurationCode.Apply(ConfigurationCode.Parse(code),target);
            Check(ConfigurationCode.Create(EffectiveSettings.ForFeed(target,feed))==code,"copy options");
            Check(!target.ImprovedAi&&!target.IndependentHostility&&target.RoamingSpawnMode=="x4","copy cannot alter stored Arcane settings");
            var packages=GamePackageSelector.Select(feed,active,false,active.CustomPlayerColors);
            bool Has(string id)=>packages.Any(p=>p.Id==id);
            Check(Has("pure-ai-improvements")==Flag(2)&&Has("pure-lair-recovery")==Flag(3),"native optional selection");
            Check(Has("pure-independent-hostility")==Flag(4)&&Has("pure-"+mod+"-hostility")==Flag(4),"relations and data paired");
            Check(Has("pure-"+mod+"-roaming-"+frequency+(Flag(5)?"-with-new":"-no-new")),"mode-native roaming");
            Check(packages.All(p=>p.Mods.Contains(mod)),"no other mod payload");
            Check(!Has("city-assistant")&&!Has("aw-hostility")&&!Has("pawpatch-core"),"excluded AW data");
            s.Channel="stable";
            Check(!EffectiveSettings.ForFeed(s,feed).PureBetaFeatures,"mismatched feed cannot enable beta features");
            s.Channel="beta";s.DataOnly=true;
            var data=EffectiveSettings.ForFeed(s,feed);
            Check(!data.PureBetaFeatures&&!data.ImprovedAi&&!data.IndependentHostility,"file-only gating");
            Check(GamePackageSelector.Select(feed,data,false,false).All(p=>p.ExecutableIndependent),"file-only payload");
            s.DataOnly=false;GameMod.SetPawPatch(s,false);
            Check(!EffectiveSettings.ForFeed(s,feed).PureBetaFeatures,"master disabled");
            GameMod.SetPawPatch(s,true);
            Check(ConfigurationCode.Create(EffectiveSettings.ForFeed(s,feed))==code,"master restores beta preferences");
            s.Mod=mod=="vanilla"?"immortals":"vanilla";
            Check(GameMod.PureComponents(s).RoamingSpawnMode=="standard","mode preference isolation");
        }
        foreach(var channel in new[]{"stable","beta"})
        foreach(var mod in new[]{"vanilla","immortals"})
        foreach(var frequency in new[]{"standard","x2","x4"})
        for(int flags=0;flags<32;flags++)
        {
            bool Flag(int i)=>(flags&(1<<i))!=0;
            var released=Feed(channel); released.PureLairRecoveryBuiltIn=true;
            released.Packages.RemoveAll(p=>p.Id=="pure-lair-recovery");
            var s=new UserSettings{Mod=mod,Channel=channel,RussianLocalization=false,GameVoiceLanguage="en"};
            GameMod.SetPawPatch(s,true);var p=GameMod.PureComponents(s);
            p.Colors=Flag(0);p.IgnoreDesync=Flag(1);p.ImprovedAi=Flag(2);p.WoundedLairDefenders=false;
            p.IndependentHostility=Flag(3);p.AdditionalRoamingCompanies=Flag(4);p.RoamingSpawnMode=frequency;
            var active=EffectiveSettings.ForFeed(s,released);var code=ConfigurationCode.Create(active);
            Check(active.PureBetaFeatures&&GameMod.PureComponents(active).WoundedLairDefenders,"promoted features and core defenders");
            Check(!p.WoundedLairDefenders,"active core feature must not overwrite legacy pinned preference");
            var packages=GamePackageSelector.Select(released,active,false,active.CustomPlayerColors);
            Check(!packages.Any(p=>p.Id=="pure-lair-recovery")&&packages.Any(p=>p.Id=="pure-beta-common"),"defenders belong to the core runtime");
            Check(packages.Any(p=>p.Id=="pure-ai-improvements")==Flag(2),"stable AI selection");
            s.DataOnly=true;
            Check(!EffectiveSettings.ForFeed(s,released).PureBetaFeatures,"promoted data-only gating");
            s.DataOnly=false;GameMod.SetPawPatch(s,false);
            Check(!EffectiveSettings.ForFeed(s,released).PureBetaFeatures,"promoted master-off gating");
        }
        Console.WriteLine("PURE BETA PASS "+count+": mode isolation, option matrix, native/data pairing, config exchange and legacy gating");
        return count;
    }
}
