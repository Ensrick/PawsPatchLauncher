using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using PawsPatchLauncher;

// Only pure selection, isolated installation and explicit --preflight modes.
// Never invokes a helper's normal entry point or writes to the source game.
public static class September20Tests
{
    static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    static ChannelManifest Feed(string path,string key)
    {
        var e=JsonSerializer.Deserialize(File.ReadAllBytes(path),LauncherJsonContext.Default.SignedFeedEnvelope)!;
        var b=Convert.FromBase64String(e.Payload);Require(CryptoAndIO.VerifySignature(b,e.Signature,key),"Invalid feed signature");
        return JsonSerializer.Deserialize(b,LauncherJsonContext.Default.ChannelManifest)!;
    }
    static UserSettings Selection(int mask,string channel="stable") => new(){Mod=GameMod.ArcaneWars,Channel=channel,PawPatchEnabled=true,
        CustomPlayerColors=(mask&1)!=0,DesyncMode=(mask&2)!=0?"continue":"official",IndependentHostility=(mask&4)!=0,
        RussianLocalization=false,GameTextLanguage="en",GameVoiceLanguage="en",RoamingSpawnMode="x4",AdditionalRoamingCompanies=true,
        SiegeBalance=true,DisablePowersAndShards=true,LargeMapSizes=true};
    public static async Task RunAsync(string repoRoot,string stageRoot,string gameRoot)
    {
        var repo=Path.GetFullPath(repoRoot);var stage=Path.GetFullPath(stageRoot);var root=Path.Combine(stage,"install-fixture");
        Require(!Directory.Exists(root),"Use a fresh fixture");
        Require(!root.Equals(Path.GetFullPath(gameRoot),StringComparison.OrdinalIgnoreCase),"Refusing the actual game");
        Directory.CreateDirectory(root);
        var config=JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(repo,"src/PawsPatchLauncher/launcher.config.json")),LauncherJsonContext.Default.LauncherConfiguration)!;
        var feed=Feed(Path.Combine(stage,"publication/test-feeds/stable.json"),config.PublicKeyPem);
        var old=Feed(Path.Combine(stage,"publication/previous/stable.json"),config.PublicKeyPem);
        var beta=Feed(Path.Combine(stage,"publication/test-feeds/beta.json"),config.PublicKeyPem);
        bool promoted=feed.PatchGuide?.Version=="0.4.0";
        bool largeMaps=beta.PatchGuide?.Version=="0.4.1";
        bool observerInput=promoted||beta.PatchGuide?.Version=="0.4.0-beta.10";
        bool september24=observerInput||beta.PatchGuide?.Version is "0.4.0-beta.3" or "0.4.0-beta.4" or "0.4.0-beta.5" or "0.4.0-beta.6" or "0.4.0-beta.7" or "0.4.0-beta.8" or "0.4.0-beta.9";
        string[] hotkeyPaths={"data/Localization/Hotkeys/hotkeys_favorites.txt","Local_base_ru/Localization/Hotkeys/hotkeys_favorites.txt","Local_ru/Localization/Hotkeys/hotkeys_favorites.txt"};
        string? hotkeyHash=observerInput?await CryptoAndIO.Sha256Async(Path.Combine(stage,"hotkeys",hotkeyPaths[0])):null;
        bool nightmare=september24||beta.PatchGuide?.Version=="0.4.0-beta.2";
        Require(feed.Channel=="stable"&&feed.PatchGuide!.Version==(promoted?"0.4.0":"0.3.3"),"Wrong release identity");
        if(promoted) {
            Require(beta.PatchGuide?.Version==(largeMaps?"0.4.1":"0.4.0"),"Unexpected Arcane beta identity");
            Require(ModChannelSelection.HasDistinctBeta(GameMod.ArcaneWars,feed,beta)==largeMaps,"Arcane beta visibility differs from its packages");
            foreach(string mod in new[]{GameMod.Vanilla,GameMod.Immortals})
                Require(ModChannelSelection.HasDistinctBeta(mod,feed,beta),"Pure beta disappeared");
        }
        Require(feed.PatchGuide!.Entries.All(e=>e.Category!="beta"),"Promoted features still beta-only");
        var installer=new ModuleInstaller(root);var cache=Path.Combine(stage,"test-cache");Directory.CreateDirectory(cache);
        var localArchives=Directory.EnumerateFiles(Path.GetDirectoryName(stage)!,"*.zip",SearchOption.AllDirectories)
            .Select(p=>new FileInfo(p)).GroupBy(p=>p.Length).ToDictionary(g=>g.Key,g=>g.Select(p=>p.FullName).ToArray());
        var prepared=new Dictionary<string,InstalledModule>();var siegeCosts=new Dictionary<string,double>();var siegeRequirements=new Dictionary<string,bool>();using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(5)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PawsPatchSeptember20Validation/1");
        async Task<InstalledModule> Prepare(PackageRelease p)
        {
            string key=p.Id+":"+p.Sha256;if(prepared.TryGetValue(key,out var found))return found;
            string path=p.Urls[0];
            if(Uri.TryCreate(path,UriKind.Absolute,out var u)&&u.Scheme=="https"){
                path=Path.Combine(cache,p.Sha256+".zip");
                if(!File.Exists(path)){
                    string? existing=null;
                    if(localArchives.TryGetValue(p.Size,out var candidates))foreach(var c in candidates)
                        if(await CryptoAndIO.Sha256Async(c)==p.Sha256){existing=c;break;}
                    if(existing is not null)path=existing;
                    else {await File.WriteAllBytesAsync(path,await http.GetByteArrayAsync(u));Console.WriteLine("DOWNLOADED "+p.Id);}
                }
            }
            Require(await CryptoAndIO.Sha256Async(path)==p.Sha256,"Wrong archive: "+p.Id);
            var m=await installer.PrepareAsync(p,path);
            foreach(var f in m.Files.Where(f=>CryptoAndIO.NormalizeRelativePath(f.Path).Equals("data\\units\\gauri\\aw_maelstrom_destroyer.tgi",StringComparison.OrdinalIgnoreCase))) {
                string text=await File.ReadAllTextAsync(Path.Combine(root,".pawpatch","packages",p.Id,p.Version,"payload",f.Path));
                var match=System.Text.RegularExpressions.Regex.Match(text,@"(?im)^\s*Kingdom_points_consumed\s*=\s*([0-9.]+)\s*$");
                siegeRequirements[f.Sha256]=System.Text.RegularExpressions.Regex.IsMatch(text,@"(?im)^\s*required_properties\s*=\s*gauri_kingdom\s*$");
                siegeCosts[f.Sha256]=match.Success?double.Parse(match.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture):0;
            }
            prepared[key]=m;return m;
        }
        var scenarios=new Dictionary<string,(List<PackageRelease> Packages,UserSettings Settings,string Exe)>();int selections=0;
        foreach(var channel in new[]{feed,beta})foreach(var text in GameLanguages.Choices)foreach(var voice in GameLanguages.VoiceChoices)
        foreach(var roaming in new[]{"standard","x2","x4"})foreach(bool data in new[]{false,true})foreach(bool enabled in new[]{false,true})
        for(int mask=0;mask<8;mask++)for(int economy=0;economy<8;economy++)foreach(bool ai in new[]{false,true}){
            var raw=Selection(mask,channel.Channel);raw.DataOnly=data;raw.PawPatchEnabled=enabled;raw.ImprovedAi=ai;
            GameLanguages.SetText(raw,text);raw.GameVoiceLanguage=voice;raw.RoamingSpawnMode=roaming;
            raw.AdditionalRoamingCompanies=(economy&1)!=0;raw.SiegeBalance=(economy&2)!=0;raw.DisablePowersAndShards=(economy&4)!=0;
            var s=EffectiveSettings.ForFeed(raw,channel);var ps=GamePackageSelector.Select(channel,s,s.RussianLocalization,s.CustomPlayerColors);
            var ids=ps.Select(p=>p.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Require(ids.Contains("ai-improvements")==s.ImprovedAi,"AI package/native switch diverged");
            Require(ps.Count==ids.Count&&ps.All(p=>p.DependsOn.All(ids.Contains)),"Incomplete or duplicate selected dependencies");
            Require(ps.Any(p=>p.Id=="common-ui")==(!data&&enabled),"Native/static HUD scope");
            Require(ps.Any(p=>p.Id=="pawpatch-core")==(!data&&enabled),"Core scope");
            if(!enabled)Require(!s.CustomPlayerColors&&!s.IndependentHostility&&s.DesyncMode=="official"&&!s.AdditionalRoamingCompanies&&!s.SiegeBalance&&!s.DisablePowersAndShards,"Master-off leaked settings");
            if(data)Require(ps.All(p=>p.ExecutableIndependent),"Native module in data-only mode");
            string exe=GameExecutableSelector.Select(config,s,channel);
            if(data)Require(exe=="k2.exe","File-only mode uses a native helper");
            string key=string.Join("|",ps.Select(p=>p.Id+":"+p.Sha256).Order())+"/"+exe;
            scenarios.TryAdd(key,(ps,s,exe));selections++;
        }
        Console.WriteLine("SELECTIONS "+selections+" UNIQUE_PLANS "+scenarios.Count);
        var helperHashes = new Dictionary<string,string>();
        foreach (string c in new[] { "stable", "beta" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(stage,c+"-helpers"),"k2_paws*.exe"))
            helperHashes[Path.GetFullPath(path)] = await CryptoAndIO.Sha256Async(path);
        // Validate each archive once, then resolve installed winners for every unique plan.
        var modules=new Dictionary<string,InstalledModule>();
        foreach(var scenario in scenarios.Values)foreach(var p in scenario.Packages)
            if(!modules.ContainsKey(p.Sha256))modules[p.Sha256]=await Prepare(p);
        foreach(var scenario in scenarios.Values){
            var winners=new Dictionary<string,ModuleFile>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in scenario.Packages.OrderBy(p=>p.Priority).ThenBy(p=>p.Id,StringComparer.OrdinalIgnoreCase)){
                var m=modules[p.Sha256];foreach(var n in m.Remove)winners.Remove(CryptoAndIO.NormalizeRelativePath(n));
                foreach(var f in m.Files)winners[CryptoAndIO.NormalizeRelativePath(f.Path)]=f;
            }
            if(scenario.Exe!="k2.exe")Require(winners.ContainsKey(scenario.Exe),"Missing selected helper: "+scenario.Exe);
            if(scenario.Settings.DataOnly)GameCompatibilityPolicy.ValidateDataModules(scenario.Packages.Select(p=>modules[p.Sha256]));
            if(scenario.Settings.PawPatchEnabled&&!scenario.Settings.DataOnly){
                if(largeMaps) {
                    string expected=scenario.Settings.Channel=="beta"?"339307F847E16C4197986D834526ED946D33062FF13F889EC19B88AF10480D9E":"DFD1B5E15507D9FE70DA7CAEDE5874DD0FC2E420B467BD662C5DDDE8DF6655E8";
                    Require(winners["data\\templates\\template_rmc_k2.tgi"].Sha256==expected,"Map template shadowed or leaked across channels");
                }
                string source=Path.Combine(stage,scenario.Settings.Channel+"-helpers",scenario.Exe);
                Require(winners[scenario.Exe].Sha256==helperHashes[source],"Stale helper wins an overlay");
                Require(winners.Keys.Count(n=>n.StartsWith("skins\\",StringComparison.OrdinalIgnoreCase)&&n.EndsWith("\\Background.tga",StringComparison.OrdinalIgnoreCase))==18,"Missing minimap frames");
            }
            if(promoted||scenario.Settings.Channel=="beta") {
                var settings=scenario.Settings;
                if(observerInput&&settings.PawPatchEnabled&&!settings.DataOnly)
                    foreach(string path in hotkeyPaths)
                        Require(winners.TryGetValue(CryptoAndIO.NormalizeRelativePath(path),out var f)&&f.Sha256==hotkeyHash,"Recruitment keys shadowed: "+path+" "+ConfigurationCode.Create(settings));
                if(nightmare) {
                    bool enabled=settings.PawPatchEnabled&&!settings.DataOnly&&settings.ImprovedAi;
                    foreach(string n in new[]{"data\\game\\handicaps_paws_nightmare.tgi","data\\properties\\paws_handicap_nightmare.tgi"})
                        Require(winners.ContainsKey(n)==enabled,"Nightmare option scope: "+ConfigurationCode.Create(settings));
                    if(settings.PawPatchEnabled&&!settings.DataOnly){
                        foreach(var row in JsonDocument.Parse(File.ReadAllText(Path.Combine(repo,"game/foundation-placement/data/manifest.json"))).RootElement.EnumerateArray()){
                            string path=CryptoAndIO.NormalizeRelativePath(row.GetProperty("path").GetString()!);
                            string expected=largeMaps&&settings.Channel=="beta"&&path.Equals("data\\templates\\template_rmc_k2.tgi",StringComparison.OrdinalIgnoreCase)
                                ?"339307F847E16C4197986D834526ED946D33062FF13F889EC19B88AF10480D9E":row.GetProperty("after").GetString()!;
                            Require(winners[path].Sha256.Equals(expected,StringComparison.OrdinalIgnoreCase),"Foundation data shadowed: "+path);
                        }
                        Require(winners.ContainsKey("data\\ui\\game\\pawgoldsound.png"),"Missing sound button");
                        if(september24) Require(winners.ContainsKey("data\\audio\\paws_gold_button.tgi"),"Missing overlapping button audio");
                        foreach(string n in new[]{"data\\ui\\game\\controlpanel\\background.tga","data\\ui\\800\\game\\controlpanel\\background.tga","data\\ui\\1280\\game\\controlpanel\\background.tga"})
                            Require(winners.ContainsKey(n),"Missing observer frame");
                    }
                }
                string siegePath="data\\units\\gauri\\aw_maelstrom_destroyer.tgi";
                Require(siegeCosts[winners[siegePath].Sha256]==(settings.SiegeBalance?0.75:0),"Maelstrom balance toggle mismatch: "+ConfigurationCode.Create(settings));
                if(september24 && settings.SiegeBalance) Require(siegeRequirements[winners[siegePath].Sha256],"Missing Gauri kingdom prerequisite: "+ConfigurationCode.Create(settings));
                if(settings.PawPatchEnabled) {
                    var presentation=modules[beta.Packages.Single(p=>p.Id=="pawpatch-core").Sha256];
                    var paths=new[]{"eagle","snowowl","vulture","aw_duck","aw_ikaris","aw_raven","aw_spineling","lake_fish"}
                        .Select(n=>"data\\units\\ambient\\"+n+".tgi").Append("data\\structures\\slaanrienclaveaw\\aw_slaanri_enclave.tgi").ToHashSet(StringComparer.OrdinalIgnoreCase);
                    foreach(var f in presentation.Files.Where(f=>paths.Contains(CryptoAndIO.NormalizeRelativePath(f.Path))))
                        Require(winners[CryptoAndIO.NormalizeRelativePath(f.Path)].Sha256==f.Sha256,"Presentation data shadowed by an option");
                }
                if(settings.PawPatchEnabled && !settings.DataOnly) foreach(var row in JsonDocument.Parse(File.ReadAllText(Path.Combine(repo,"game/ai-policy/data/manifest.json"))).RootElement.EnumerateArray()) {
                    string path=CryptoAndIO.NormalizeRelativePath(row.GetProperty("path").GetString()!);
                    string expected=row.GetProperty(settings.ImprovedAi?"after":"before").GetString()!;
                    Require(winners[path].Sha256.Equals(expected,StringComparison.OrdinalIgnoreCase),"AI data mismatch: "+path+" "+ConfigurationCode.Create(settings));
                }
                if(settings.PawPatchEnabled&&!settings.DataOnly){
                    string id=GameLanguages.Text(settings)=="en"?"common-ui":"localization-bot-ui-"+GameLanguages.Text(settings);
                    var ui=modules[scenario.Packages.Single(p=>p.Id==id).Sha256];
                    if(september24) foreach(var f in ui.Files.Where(f=>f.Path.Replace('\\','/').EndsWith("/localization/strings_data_k2.tgi",StringComparison.OrdinalIgnoreCase)))
                        Require(winners[CryptoAndIO.NormalizeRelativePath(f.Path)].Sha256==f.Sha256,"Localized cost/button tooltips shadowed: "+ConfigurationCode.Create(settings));
                    foreach(var name in new[]{"pcolors","staging"}){
                        string path="data\\ui\\menus\\"+name+".tgi";
                        Require(winners[path].Sha256==ui.Files.Single(f=>CryptoAndIO.NormalizeRelativePath(f.Path).Equals(path,StringComparison.OrdinalIgnoreCase)).Sha256,"Wrong localized lobby template");
                    }
                }
            }

        }
        await File.WriteAllTextAsync(Path.Combine(root,"save-sentinel.rsg"),"preserve save and preferences");
        File.Copy(Path.Combine(gameRoot,"k2.exe"),Path.Combine(root,"k2.exe"));string stock=await CryptoAndIO.Sha256Async(Path.Combine(root,"k2.exe"));
        int transitions=0,preflights=0,frameChecks=0;
        async Task Apply(ChannelManifest channel,UserSettings raw,bool preflight){
            var s=EffectiveSettings.ForFeed(raw,channel);var desired=new Dictionary<string,InstalledModule>();
            foreach(var p in GamePackageSelector.Select(channel,s,s.RussianLocalization,s.CustomPlayerColors))desired[p.Id]=await Prepare(p);
            await installer.ReconcileAsync(desired,settings:s);Require((await installer.VerifyAsync()).Count==0,"Installed file verification failed");transitions++;
            if(largeMaps&&s.PawPatchEnabled&&!s.DataOnly) {
                string expected=channel==beta?"339307F847E16C4197986D834526ED946D33062FF13F889EC19B88AF10480D9E":"DFD1B5E15507D9FE70DA7CAEDE5874DD0FC2E420B467BD662C5DDDE8DF6655E8";
                Require(await CryptoAndIO.Sha256Async(Path.Combine(root,"data/Templates/template_rmc_k2.tgi"))==expected,"Installed map template did not follow channel transition");
                Require((await File.ReadAllLinesAsync(Path.Combine(root,"paws_patch_versions.ini"))).Contains("PawPatch="+channel.PatchGuide!.Version),"Installed patch identity differs from channel");
            }
            bool current=channel==feed||channel==beta;
            if(observerInput&&(channel==beta||promoted&&current)&&s.PawPatchEnabled&&!s.DataOnly)
                foreach(string path in hotkeyPaths)
                    Require(await CryptoAndIO.Sha256Async(Path.Combine(root,path))==hotkeyHash,"Installed recruitment keys shadowed: "+path);
            if(nightmare){
                bool enabled=GameMod.HasImprovedAi(channel)&&s.PawPatchEnabled&&!s.DataOnly&&s.ImprovedAi;
                foreach(string n in new[]{"data/game/handicaps_paws_nightmare.tgi","data/properties/paws_handicap_nightmare.tgi"})
                    Require(File.Exists(Path.Combine(root,n))==enabled,"Nightmare survived an option/channel transition");
                if(enabled){
                    string lang=GameLanguages.Text(s);
                    string locale=Path.Combine(root,lang=="en"?"data/Localization/paws_nightmare.tgi":"Local_ru/Localization/paws_nightmare.tgi");
                    Require(File.Exists(locale)&&File.ReadAllText(locale).Contains("paws_handicap_nightmare_name"),"Missing installed Nightmare translation");
                }
            }
            if(s.PawPatchEnabled&&!s.DataOnly&&channel==feed){
                Require((await File.ReadAllLinesAsync(Path.Combine(root,"paws_patch_versions.ini"))).Contains("PawPatch="+feed.PatchGuide!.Version),"Wrong menu identity");
                var frames=desired["common-ui"].Files.Where(f=>f.Path.Replace('\\','/').StartsWith("skins/")&&f.Path.EndsWith("/background.tga",StringComparison.OrdinalIgnoreCase)).ToList();
                Require(frames.Count==18,"Missing installed frames");foreach(var f in frames){Require(await CryptoAndIO.Sha256Async(Path.Combine(root,f.Path))==f.Sha256,"Frame mismatch");frameChecks++;}
            }
            if(preflight){
                string exe=GameExecutableSelector.Select(config,s,channel);
                using var process=Process.Start(new ProcessStartInfo(Path.Combine(root,exe)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,ArgumentList={"--preflight",root}})!;
                string output=await process.StandardOutput.ReadToEndAsync(),error=await process.StandardError.ReadToEndAsync();await process.WaitForExitAsync();
                if(channel==beta||promoted&&current)Require(output.Contains("AI_IMPROVEMENTS "+(s.ImprovedAi?"on":"off")),"Wrong native AI activation: "+exe+" "+GameLanguages.Text(s)+" "+output+" "+error);
                if((channel==beta||promoted&&current)&&nightmare)Require(output.Contains("NIGHTMARE_DIFFICULTY "+(s.ImprovedAi?"on":"off")),"Wrong Nightmare activation");
                Require(process.ExitCode==0&&output.Contains("PREFLIGHT_PASS"),"Preflight failed: "+exe+" "+error);preflights++;
            }
            Console.WriteLine("TRANSITION "+transitions+" "+channel.Channel+" "+GameLanguages.Text(s)+" ai="+s.ImprovedAi+" data="+s.DataOnly+" patch="+s.PawPatchEnabled+" preflights="+preflights);
        }
        await Apply(old,Selection(7),false);
        // Beta.3 changes no stable package. Its complete selection matrix still
        // covers stable; real install transitions focus on the changed beta,
        // with stable installation before and rollback/uninstall afterwards.
        if(promoted) for(int mask=0;mask<8;mask++)foreach(bool ai in new[]{true,false}){
            var s=Selection(mask);s.ImprovedAi=ai;await Apply(feed,s,true);
        }
        if(!september24) for(int mask=0;mask<8;mask++)await Apply(feed,Selection(mask),true);
        File.Copy(Path.Combine(root,".pawpatch/state.json"),Path.Combine(stage,"installed-033-state.json"),true);
        if(promoted||!september24) for(int i=0;i<GameLanguages.Choices.Count;i++){
            var s=Selection(i%8);GameLanguages.SetText(s,GameLanguages.Choices[i]);s.GameVoiceLanguage=GameLanguages.VoiceChoices[i%4];
            s.RoamingSpawnMode=new[]{"standard","x2","x4"}[i%3];s.AdditionalRoamingCompanies=i%2==0;s.SiegeBalance=i%2!=0;s.DisablePowersAndShards=i%3==0;
            await Apply(feed,s,true);
            s.DataOnly=true;await Apply(feed,s,false);
            Require(!File.Exists(Path.Combine(root,"k2_paws_ui_1372.exe")),"Runtime survived file-only transition");
            s.DataOnly=false;s.PawPatchEnabled=false;await Apply(feed,s,false);
            Require(!File.Exists(Path.Combine(root,"d3d9.dll")),"Graphics runtime survived master-off");
        }
        // Every runtime variant with AI on and off, then all localized lobby overlays.
        if(observerInput){
            var previousBeta=Feed(Path.Combine(stage,"publication/previous/beta.json"),config.PublicKeyPem);
            var upgrade=Selection(7,"beta");GameLanguages.SetText(upgrade,"ru");
            await Apply(previousBeta,upgrade,false);await Apply(beta,upgrade,true);
            await Apply(previousBeta,upgrade,false);await Apply(beta,upgrade,true);
        }
        for(int mask=0;mask<8;mask++)foreach(bool ai in new[]{true,false}){
            var s=Selection(mask,"beta");s.ImprovedAi=ai;await Apply(beta,s,true);
        }
        foreach(string text in GameLanguages.Choices){
            var s=Selection(7,"beta");GameLanguages.SetText(s,text);s.ImprovedAi=true;await Apply(beta,s,true);
            s.ImprovedAi=false;await Apply(beta,s,true);
            s.DataOnly=true;await Apply(beta,s,false);
            Require(!File.Exists(Path.Combine(root,"data/UI/Menus/staging.tgi")),"Bot setup survived data-only mode");
            s.DataOnly=false;s.PawPatchEnabled=false;await Apply(beta,s,false);
            Require(!File.Exists(Path.Combine(root,"data/UI/Menus/staging.tgi")),"Bot setup survived master-off");
        }
        await Apply(beta,Selection(7,"beta"),true);await Apply(feed,Selection(7),true);await Apply(old,Selection(7),false);
        Require(Directory.EnumerateFiles(Path.Combine(root,"skins"),"Background.tga",SearchOption.AllDirectories).Count()==18,"Accepted frames missing after rollback to 0.3.0");
        Require(!GameCompatibilityPolicy.Supports(feed.Game,new string('0',64)),"Unknown EXE accepted");
        var mismatch=Selection(7,"beta");mismatch.DataOnly=true;mismatch.ImprovedAi=true;
        var partial=EffectiveSettings.ForFeed(mismatch,beta);
        Require(!partial.ImprovedAi&&GameExecutableSelector.Select(config,partial,beta)=="k2.exe","Unknown EXE uses patched runtime");
        await installer.UninstallAsync();transitions++;
        Require(await File.ReadAllTextAsync(Path.Combine(root,"save-sentinel.rsg"))=="preserve save and preferences","Save changed");
        Require(await CryptoAndIO.Sha256Async(Path.Combine(root,"k2.exe"))==stock,"Stock executable changed");
        var report=new{passed=true,selections,uniquePlans=scenarios.Count,verifiedArchives=prepared.Count,uniqueHelpers=8,frameChecks,preflights,transitions,gameLaunched=false,sourceGameReadOnly=true};
        await File.WriteAllTextAsync(Path.Combine(stage,"installation-verification.json"),JsonSerializer.Serialize(report));Console.WriteLine(JsonSerializer.Serialize(report));
    }
}
