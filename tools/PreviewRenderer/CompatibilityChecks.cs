using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class CompatibilityChecks
{
    const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
    static object? Call(MainWindow w,string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,F)!.Invoke(w,args);
    static void Set(MainWindow w,string name,object? value)=>typeof(MainWindow).GetField(name,F)!.SetValue(w,value);
    static T Field<T>(MainWindow w,string name)=>(T)typeof(MainWindow).GetField(name,F)!.GetValue(w)!;
    internal static void Populate(MainWindow w)
    {
        if(!ActivityStore.IsSmokeTest)throw new InvalidOperationException("Fixture only");
        var settings=Field<UserSettings>(w,"_settings"); settings.Mod=GameMod.Immortals;
        if(Field<ChannelManifest?>(w,"_channel") is null) Set(w,"_channel",new ChannelManifest());
        Set(w,"_game",new GameInstallation(Path.Combine(Path.GetTempPath(),"paw-preview-no-game"),"preview.fixture","25068127","beta"));
        Set(w,"_compatibilityState",GameCompatibilityState.Unsupported);Set(w,"_compatibilityKey","preview-mismatch");
        Set(w,"_compatibilityNoticeKey","preview-mismatch");Set(w,"_installedGameVersion","1.3.73");
        Call(w,"RefreshStatus");
    }
    internal static void Run(string language)
    {
        var w=new MainWindow(new LauncherConfiguration{FeedUrls=[],BetaFeedUrls=[]},null){Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Width=1050,Height=680};
        var root=Path.Combine(Path.GetTempPath(),"paw-compatibility-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var path=Path.Combine(root,"k2.fixture");
        int n=0;void Check(bool ok,string message){n++;if(!ok)throw new Exception("Compatibility UI: "+message);}
        T C<T>(string name)=>(T)w.FindName(name);
        async Task Scenario()
        {
            var settings=Field<UserSettings>(w,"_settings");settings.Mod=GameMod.Immortals;GameMod.SetPawPatch(settings,true);
            settings.RussianLocalization=false;settings.ModNoticeSeen=true;
            var pureChoice=GameMod.PureComponents(settings);pureChoice.Colors=pureChoice.IgnoreDesync=pureChoice.IndependentHostility=true;
            Field<PawsPatchLauncher.Localization>(w,"_text").SetLanguage(language);Call(w,"ApplyLanguage");
            Set(w,"_gameRunningProbe",(Func<bool>)(()=>false));
            var bytes=Encoding.Unicode.GetBytes("\0Kohan\01.3.72\0");await File.WriteAllBytesAsync(path,bytes);
            var hash=Convert.ToHexString(SHA256.HashData(bytes));
            Set(w,"_game",new GameInstallation(root,path,"25068126","beta"));
            var channel=new ChannelManifest{PureRuntimeOptions=true,ColorDesyncContinue=true,IndependentColorHostility=true,Game=new GameRequirement{Version="1.3.72",SteamBuild="25068126",K2ExeSha256=[hash]}};
            foreach(var id in new[]{"startup-base","immortals","pure-fixes-data","pure-fixes-runtime","pure-player-colors"})
                channel.Packages.Add(new(){Id=id,Version="1",Sha256=new('A',64),ExecutableIndependent=id is "startup-base" or "immortals" or "pure-fixes-data",
                    Mods=id is "pure-player-colors" ? [GameMod.Vanilla,GameMod.Immortals] : []});
            Set(w,"_channel",channel);Call(w,"RefreshModuleAvailability");
            Check(await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!,"supported hash rejected");
            Check(C<TextBlock>("InstalledGameText").Text=="1.3.72","installed game version missing");
            await File.WriteAllBytesAsync(path,Encoding.Unicode.GetBytes("\0Kohan\01.3.73\0"));
            Check(!await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!,"unsupported file accepted");
            Check(Field<Border?>(w,"_compatibilityPopup") is null,"An uninstalled mod opened an automatic update warning");
            C<Button>("GameCompatibilityButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var popup=Field<Border>(w,"_compatibilityPopup");
            Check(popup is not null&&!C<Button>("LaunchButton").IsEnabled&&C<TextBlock>("GameCompatibilityHint").Visibility==Visibility.Visible,"warning / launch gate absent");
            foreach(var name in new[]{"ColorsToggle","IndependentHostilityToggle","IgnoreDesyncToggle"})
                Check(!C<CheckBox>(name).IsEnabled&&C<CheckBox>(name).IsChecked==false,"native option not visibly locked: "+name);
            Check(pureChoice.Colors&&pureChoice.IndependentHostility&&pureChoice.IgnoreDesync,"native choices erased");
            Check(C<Button>("PawCompatibilityButton").Visibility==Visibility.Visible,"partial Paw details missing");
            Check(C<Border>("ModulesNavBadge").Visibility==Visibility.Visible,"compatibility unread badge missing");
            Check(((string)Call(w,"CompatibilityExplanation")!).Contains(language=="ru"?"новее":"newer"),"newer-game guidance missing");
            for(var i=0;i<3;i++)Call(w,"RefreshGameLaunchState");
            Check(ReferenceEquals(popup,Field<Border>(w,"_compatibilityPopup")),"warning rebuilt every tick");
            ((DockPanel)((StackPanel)popup!.Child).Children[0]).Children.OfType<Button>().Single().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(300);Call(w,"RefreshGameLaunchState");
            Check(Field<Border?>(w,"_compatibilityPopup")==null&&!C<Button>("LaunchButton").IsEnabled,"dismissal enabled launch or popup respawned");
            Check(C<CheckBox>("PawPatchToggle").IsEnabled&&!C<CheckBox>("ColorsToggle").IsEnabled,"partial Paw/native control lock incorrect after dismissal");
            Call(w,"SetActivePage","modules");Call(w,"VisitComponents");
            Check(C<Border>("ModulesNavBadge").Visibility==Visibility.Collapsed&&C<Border>("CompatibilityBanner").Visibility==Visibility.Visible,"badge/banner lifecycle");
            await (Task)Call(w,"LaunchGameAsync")!;
            Check(!Field<bool>(w,"_launchStarting")&&!C<Button>("LaunchButton").IsEnabled,"direct launch bypassed explicit Apply gate");
            Call(w,"CloseCompatibilityPopup");
            var effective=(UserSettings)Call(w,"GetEffectiveSettings")!;
            var state=new InstallState{AppliedSettings=effective,BaseGameSha256=await CryptoAndIO.Sha256Async(path),GameRequirement=channel.Game};
            foreach(var p in GamePackageSelector.Select(channel,effective,false,false))state.Modules[p.Id]=new(){Enabled=true,Version=p.Version,ArchiveSha256=p.Sha256,Priority=p.Priority};
            Directory.CreateDirectory(Path.Combine(root,".pawpatch"));
            await File.WriteAllTextAsync(Path.Combine(root,".pawpatch/state.json"),JsonSerializer.Serialize(state,LauncherJsonContext.Default.InstallState));
            Set(w,"_selectedInstalledRelease",channel);
            await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!;
            Check(!Field<bool>(w,"_settingsPending")&&!Field<bool>(w,"_installedRuntimeMismatch"),"applied limited configuration still requires reconciliation");
            Call(w,"ShowCompatibilityPopup",true);
            Check(((string)Call(w,"PartialPawDescription")!).Contains(language=="ru"?"Недоступны":"Unavailable"),"partial feature lists absent");
            Call(w,"CloseCompatibilityPopup");
            var offered=JsonSerializer.Deserialize(JsonSerializer.Serialize(channel,LauncherJsonContext.Default.ChannelManifest),LauncherJsonContext.Default.ChannelManifest)!;
            offered.Game.K2ExeSha256=[state.BaseGameSha256!];Set(w,"_latestChannel",offered);
            Set(w,"_selectedInstalledRelease",channel);
            Check(!await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!,"archived incompatible feed was silently replaced");
            Check(Field<ChannelManifest?>(w,"_compatiblePatchUpdate") is not null&&!C<Button>("LaunchButton").IsEnabled,"limited installation not offered compatible update");
            popup=Field<Border>(w,"_compatibilityPopup");
            Check(((StackPanel)popup!.Child).Children.OfType<Button>().Single().Content.ToString()==(language=="ru"?"Обновить патч":"Update patch"),"red dialog lacks update action");
            Call(w,"CloseCompatibilityPopup");
            Set(w,"_channel",offered);
            Check(await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!,"new compatible feed rejected");
            Check(C<CheckBox>("ColorsToggle").IsChecked==true&&C<CheckBox>("IndependentHostilityToggle").IsChecked==true&&C<CheckBox>("IgnoreDesyncToggle").IsChecked==true,"native preferences not restored for compatible patch");
            foreach(var mod in new[]{GameMod.Vanilla,GameMod.Immortals})
            {
                Call(w,"CloseCompatibilityPopup");
                settings.Mod=mod;GameMod.SetPawPatch(settings,false);
                var pure=new ChannelManifest{Game=new GameRequirement{Version="1.3.72",K2ExeSha256=[hash]}};
                foreach(var id in new[]{"startup-base","immortals","pure-fixes-data","pure-fixes-runtime","immortals-text-fixes"})
                    pure.Packages.Add(new(){Id=id,Version="1",Sha256=new('A',64),ExecutableIndependent=id!="pure-fixes-runtime"});
                var pureOffer=JsonSerializer.Deserialize(JsonSerializer.Serialize(pure,LauncherJsonContext.Default.ChannelManifest),LauncherJsonContext.Default.ChannelManifest)!;
                pureOffer.Game.K2ExeSha256=[state.BaseGameSha256!];
                Set(w,"_channel",pure);Set(w,"_latestChannel",pureOffer);
                var pureState=new InstallState{AppliedSettings=EffectiveSettings.ForFeed(settings,pure),GameRequirement=pure.Game};
                foreach(var p in GamePackageSelector.Select(pure,pureState.AppliedSettings,false,false))
                    pureState.Modules[p.Id]=new(){Enabled=true,Version=p.Version,ArchiveSha256=p.Sha256,Priority=p.Priority};
                await File.WriteAllTextAsync(Path.Combine(root,".pawpatch/state.json"),JsonSerializer.Serialize(pureState,LauncherJsonContext.Default.InstallState));
                await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!;
                Check(Field<ChannelManifest?>(w,"_compatiblePatchUpdate")==null,"disabled Pure offers a runtime update");
                var disabledKey=Field<string>(w,"_compatibilityKey");
                C<CheckBox>("PawPatchToggle").IsChecked=true;Call(w,"PawPatchChanged",C<CheckBox>("PawPatchToggle"),new RoutedEventArgs());
                Set(w,"_selectedInstalledRelease",pure);
                await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",false)!;
                Check(Field<string>(w,"_compatibilityKey")!=disabledKey,"enabling Pure reused the disabled compatibility result");
                Check(Field<ChannelManifest?>(w,"_compatiblePatchUpdate") is not null,"enabling Pure failed to offer an available compatible update");
                Call(w,"RefreshModControls");
                Check(C<Border>("CoreModuleCard").Visibility==Visibility.Visible&&C<Border>("VanillaEmptyCard").Visibility==Visibility.Collapsed,"Pure card overlaps the no-components placeholder");
                var pureEffective=(UserSettings)Call(w,"GetEffectiveSettings")!;
                var packages=GamePackageSelector.Select(pure,pureEffective,false,false);
                Check(packages.Any(p=>p.Id=="pure-fixes-data")&&!packages.Any(p=>p.Id=="pure-fixes-runtime"),"incompatible Pure retained runtime changes or lost file fixes");
                if(mod==GameMod.Immortals)
                {
                    var help=(string)Call(w,"PureFixesDescription",true)!;
                    Check(help.IndexOf("Immortals",StringComparison.Ordinal)<help.IndexOf(language=="ru"?"Недоступны":"Unavailable",StringComparison.Ordinal),"available Immortals labels listed under unavailable functions");
                }
            }
            Call(w,"CloseCompatibilityPopup");
            Set(w,"_game",new GameInstallation(root,path+".missing",null,null));
            Check(!await (Task<bool>)Call(w,"CheckGameCompatibilityAsync",true)!,"missing binary accepted");
            Check(Field<GameCompatibilityState>(w,"_compatibilityState")==GameCompatibilityState.Unavailable&&!C<Button>("LaunchButton").IsEnabled,"unreadable executable unlocked launch");
        }
        try
        {
            w.Show();var task=w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();var frame=new DispatcherFrame();
            var timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};timeout.Tick+=(_,_)=>frame.Continue=false;
            task.ContinueWith(_=>w.Dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
            timeout.Start();Dispatcher.PushFrame(frame);timeout.Stop();if(!task.IsCompleted)throw new TimeoutException();task.GetAwaiter().GetResult();
            Console.WriteLine($"COMPATIBILITY UI PASS {n} {language}: synthetic versions; explicit Apply, remembered choices, compatible update; game never launched");
        }
        finally{w.Close();Directory.Delete(root,true);}
    }
}
