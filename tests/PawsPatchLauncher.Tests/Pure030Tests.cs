using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using PawsPatchLauncher;

// Exercise signed production candidates through the actual transactional installer.
// The live installation supplies only a read-only copy of its stock executable.
internal static class Pure030Tests
{
    static int checks;
    static void Check(bool ok, string why) { checks++; if (!ok) throw new InvalidDataException(why); }
    static ChannelManifest Feed(string path, string key)
    {
        var e=JsonSerializer.Deserialize(File.ReadAllBytes(path),LauncherJsonContext.Default.SignedFeedEnvelope)!;
        byte[] data=Convert.FromBase64String(e.Payload);
        Check(CryptoAndIO.VerifySignature(data,e.Signature,key),"Catalog signature");
        return JsonSerializer.Deserialize(data,LauncherJsonContext.Default.ChannelManifest)!;
    }
    static UserSettings Selection(string mod,string channel,int mask,bool patch=true,bool data=false,string language="en")
    {
        var s=new UserSettings {Mod=mod,Channel=channel,DataOnly=data,GameTextLanguage=language,GameVoiceLanguage="en",RussianLocalization=language=="ru"};
        GameMod.SetPawPatch(s,true);GameMod.SetColors(s,(mask&1)!=0);GameMod.SetDesync(s,(mask&2)!=0);
        if(!patch)GameMod.SetPawPatch(s,false);
        return s;
    }
    internal static async Task RunAsync(string repoArg,string stageArg,string gameRoot)
    {
        string repo=Path.GetFullPath(repoArg),stage=Path.GetFullPath(stageArg),root=Path.Combine(stage,"install-fixture");
        Check(!Directory.Exists(root)&&!root.Equals(Path.GetFullPath(gameRoot),StringComparison.OrdinalIgnoreCase),"Fresh isolated fixture required");
        Directory.CreateDirectory(root);
        var config=JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(repo,"src/PawsPatchLauncher/launcher.config.json")),LauncherJsonContext.Default.LauncherConfiguration)!;
        var feeds=new[]{"stable","beta"}.ToDictionary(c=>c,c=>Feed(Path.Combine(stage,"publication/test-feeds",c+".json"),config.PublicKeyPem));
        var previous=new[]{"stable","beta"}.ToDictionary(c=>c,c=>Feed(Path.Combine(stage,"publication/previous",c+".json"),config.PublicKeyPem));
        var ids=new HashSet<string>{"pure-fixes-data","pure-fixes-runtime","pure-player-colors"};
        int selections=0,transitions=0,preflights=0;
        foreach(string channel in feeds.Keys)
        {
            var f=feeds[channel];
            Check(f.Packages.Where(p=>!ids.Contains(p.Id)).Select(p=>p.Id+p.Sha256).SequenceEqual(previous[channel].Packages.Where(p=>!ids.Contains(p.Id)).Select(p=>p.Id+p.Sha256)),"Non-pure package changed");
            foreach(string mod in new[]{GameMod.Vanilla,GameMod.Immortals})
            {
                Check(f.ModGuides.Single(g=>g.Id==mod).PatchGuide!.Version=="0.3.0","Pure guide version");
                Check(!ModChannelSelection.HasDistinctBeta(mod,feeds["stable"],feeds["beta"]),"Promoted beta remains advertised");
                foreach(string language in new[]{"en","ru","de","fr","cs","uk"})
                foreach(bool data in new[]{false,true})foreach(bool enabled in new[]{false,true})for(int mask=0;mask<4;mask++)
                {
                    var s=EffectiveSettings.ForFeed(Selection(mod,channel,mask,enabled,data,language),f);
                    var ps=GamePackageSelector.Select(f,s,s.RussianLocalization,s.CustomPlayerColors);
                    Check(!data||ps.All(p=>p.ExecutableIndependent),"Native package in file-only mode");
                    Check(enabled||!ps.Any(p=>ids.Contains(p.Id)),"Master-off retained pure package");
                    Check(ps.Any(p=>p.Id=="pure-player-colors")==((mask&1)!=0&&enabled&&!data),"Color package scope");
                    Check((s.DesyncMode=="continue")==((mask&2)!=0&&enabled&&!data),"Diagnostic switch scope");
                    selections++;
                }
            }
        }
        var installer=new ModuleInstaller(root);string cache=Path.Combine(stage,"test-cache");Directory.CreateDirectory(cache);
        var prepared=new Dictionary<string,InstalledModule>();using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(4)};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PawsPure030Acceptance/1");
        async Task<InstalledModule> Prepare(PackageRelease p)
        {
            string key=p.Id+p.Sha256;if(prepared.TryGetValue(key,out var found))return found;
            string path=p.Urls[0];
            if(Uri.TryCreate(path,UriKind.Absolute,out var u)&&u.Scheme=="https")
            {
                path=Path.Combine(cache,p.Sha256+".zip");
                if(!File.Exists(path))foreach(string folder in Directory.EnumerateDirectories(Path.GetDirectoryName(stage)!))
                {
                    string known=Path.Combine(folder,"test-cache",p.Sha256+".zip");
                    if(File.Exists(known)){File.Copy(known,path);break;}
                }
                if(!File.Exists(path))await File.WriteAllBytesAsync(path,await http.GetByteArrayAsync(u));
            }
            Check(await CryptoAndIO.Sha256Async(path)==p.Sha256,"Archive digest: "+p.Id);
            return prepared[key]=await installer.PrepareAsync(p,path);
        }
        async Task<Dictionary<string,InstalledModule>> Install(ChannelManifest f,UserSettings raw)
        {
            var s=EffectiveSettings.ForFeed(raw,f);var desired=new Dictionary<string,InstalledModule>();
            foreach(var p in GamePackageSelector.Select(f,s,s.RussianLocalization,s.CustomPlayerColors))desired[p.Id]=await Prepare(p);
            await installer.ReconcileAsync(desired,settings:s);transitions++;
            Check((await installer.VerifyAsync()).Count==0,"Installed verification");
            return desired;
        }
        File.Copy(Path.Combine(gameRoot,"k2.exe"),Path.Combine(root,"k2.exe"));string stock=await CryptoAndIO.Sha256Async(Path.Combine(root,"k2.exe"));
        await File.WriteAllTextAsync(Path.Combine(root,"save-sentinel.rsg"),"preserved");
        foreach(string mod in new[]{GameMod.Vanilla,GameMod.Immortals})
        {
            foreach(string channel in feeds.Keys)
            {
                await Install(previous[channel],Selection(mod,channel,3));
                for(int mask=0;mask<4;mask++)
                {
                    var f=feeds[channel];var s=EffectiveSettings.ForFeed(Selection(mod,channel,mask),f);
                    var installed=await Install(f,s);string exe=GameExecutableSelector.Select(config,s,f);
                    Check(await CryptoAndIO.Sha256Async(Path.Combine(root,exe))==await CryptoAndIO.Sha256Async(Path.Combine(stage,"build/stable",exe)),"Correct helper");
                    Check(installed["pure-fixes-data"].Files.Count(x=>x.Path.StartsWith("skins/",StringComparison.OrdinalIgnoreCase))==18,"Six racial frames in three sizes");
                    using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,exe)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,ArgumentList={"--features"}})!)
                    {
                        var feature=JsonDocument.Parse(await process.StandardOutput.ReadToEndAsync());await process.WaitForExitAsync();
                        Check(feature.RootElement.GetProperty("patchVersion").GetString()=="0.3.0","Runtime identity");
                        Check(feature.RootElement.TryGetProperty("syncDiagnosticsRevision",out var rev)==((mask&2)!=0),"Native log writer selected");
                        if((mask&2)!=0)Check(rev.GetInt32()==1,"Shared diagnostics revision");
                    }
                    using(var process=Process.Start(new ProcessStartInfo(Path.Combine(root,exe)){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,ArgumentList={"--preflight",root}})!)
                    {
                        string text=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
                        Check(process.ExitCode==0&&text.Contains("PURE_PREFLIGHT_PASS"),"Read-only preflight");preflights++;
                    }
                }
                await Install(feeds[channel],Selection(mod,channel,3,data:true));
                await Install(feeds[channel],Selection(mod,channel,3,patch:false));
                await Install(previous[channel],Selection(mod,channel,3));
            }
        }
        await installer.UninstallAsync();
        Check(await File.ReadAllTextAsync(Path.Combine(root,"save-sentinel.rsg"))=="preserved","Save changed");
        Check(await CryptoAndIO.Sha256Async(Path.Combine(root,"k2.exe"))==stock,"Stock EXE changed");
        var report=new{passed=true,checks,selections,transitions,preflights,archives=prepared.Count,gameLaunched=false,liveGameChanged=false};
        await File.WriteAllTextAsync(Path.Combine(stage,"installation-verification.json"),JsonSerializer.Serialize(report));
        Console.WriteLine("PURE030_INSTALL_PASS "+JsonSerializer.Serialize(report));
    }
}
