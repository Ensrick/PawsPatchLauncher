using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class CompactComponentsChecks
{
    internal static void Run(string configPath, string game, string language, string output)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Disposable profile required.");
        var config=JsonSerializer.Deserialize(File.ReadAllText(configPath),LauncherJsonContext.Default.LauncherConfiguration)!;
        var client=new FeedClient(config);
        var feed=Task.Run(()=>client.GetChannelAsync()).GetAwaiter().GetResult()!;
        var statePath=Path.Combine(game,".pawpatch/state.json");var original=File.ReadAllBytes(statePath);
        var state=new ModuleInstaller(game).LoadState();
        var prefs=JsonSerializer.Deserialize(JsonSerializer.Serialize(state.AppliedSettings!,LauncherJsonContext.Default.UserSettings),LauncherJsonContext.Default.UserSettings)!;
        prefs.Language=language;prefs.ModNoticeSeen=true;prefs.GamePath=game;
        new SettingsStore().Save(prefs);
        var w=new MainWindow(config,client){Left=-32000,Top=-32000,Width=1050,Height=680,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string n,object? v)=>typeof(MainWindow).GetField(n,flags)!.SetValue(w,v);
        T Field<T>(string n)=>(T)typeof(MainWindow).GetField(n,flags)!.GetValue(w)!;
        object? Call(string n,params object?[] a)=>typeof(MainWindow).GetMethod(n,flags)!.Invoke(w,a);
        T C<T>(string n)=>(T)w.FindName(n);
        var count=0;
        void Check(bool ok,string why){if(!ok)throw new Exception("Compact components: "+why);count++;}
        void Save(string name)
        {
            Directory.CreateDirectory(output);w.UpdateLayout();var bitmap=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(w);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+"-"+language+".png"));png.Save(file);
        }
        async Task Layout(){await Task.Delay(300);await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);w.UpdateLayout();}
        void Describe(string phase)
        {
            var desired=(UserSettings)Call("GetEffectiveSettings")!;
            var selected=(List<PackageRelease>)Call("ResolveSelectedPackages",Field<ChannelManifest>("_channel"))!;
            Console.WriteLine(phase+" pending="+Field<bool>("_settingsPending")+" apply="+C<Button>("ApplySettingsButton").IsVisible);
            var before=JsonSerializer.SerializeToElement(state.AppliedSettings!,LauncherJsonContext.Default.UserSettings);
            var after=JsonSerializer.SerializeToElement(desired,LauncherJsonContext.Default.UserSettings);
            foreach(var p in after.EnumerateObject())if(before.TryGetProperty(p.Name,out var old)&&old.ToString()!=p.Value.ToString())Console.WriteLine("SETTING "+p.Name+" "+old+" -> "+p.Value);
            foreach(var p in selected)if(!state.Modules.TryGetValue(p.Id,out var old)||old.Version!=p.Version||old.ArchiveSha256!=p.Sha256||old.Priority!=p.Priority)Console.WriteLine("PACKAGE "+p.Id+" "+old?.Version+" -> "+p.Version);
        }
        async Task Scenario()
        {
            Set("_channel",feed);Set("_offeredModChannel",feed);Set("_latestChannel",feed);
            Set("_game",new GameInstallation(game,Path.Combine(game,"k2.exe"),"fixture","stable"));
            Set("_compatibilityState",GameCompatibilityState.Supported);
            Call("ApplyLanguage");Call("SetActivePage","modules");Call("RefreshStatus");await Layout();Describe("initial");
            foreach(var method in new[]{"RefreshStatus","SelectLibraryChannel","RefreshConfigurationCode","RefreshGameLaunchState","RefreshModControls","RefreshCompatibilityControls","CaptureAppearance"})
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();Call(method);watch.Stop();Console.WriteLine($"TIMING {method}: {watch.Elapsed.TotalMilliseconds:F1} ms");
            }
            var text=C<ComboBox>("GameLanguageCombo");var initial=text.SelectedIndex;
            var selectionWatch=System.Diagnostics.Stopwatch.StartNew();text.SelectedIndex=1-initial;selectionWatch.Stop();Console.WriteLine($"TIMING text selection: {selectionWatch.Elapsed.TotalMilliseconds:F1} ms");await Layout();Describe("changed");
            text.SelectedIndex=initial;await Layout();Describe("restored");
            Check(!Field<bool>("_settingsPending")&&!C<Button>("ApplySettingsButton").IsVisible&&C<Button>("LaunchButton").IsEnabled,"text return left Apply or blocked Launch");
            var voice=C<ComboBox>("GameVoiceCombo");var originalVoice=voice.SelectedIndex;
            selectionWatch.Restart();voice.SelectedIndex=1-originalVoice;selectionWatch.Stop();Console.WriteLine($"TIMING speech selection: {selectionWatch.Elapsed.TotalMilliseconds:F1} ms");await Layout();
            Check(Field<bool>("_settingsPending")&&C<Button>("ApplySettingsButton").IsEnabled&&!C<Button>("LaunchButton").IsEnabled,"voice change did not require Apply");
            voice.SelectedIndex=originalVoice;await Layout();
            Check(!C<Button>("ApplySettingsButton").IsVisible&&C<Button>("LaunchButton").IsEnabled,"voice return left Apply");
            for(var i=0;i<4;i++)
            {
                text.SelectedIndex=1-initial;voice.SelectedIndex=1-originalVoice;
                text.SelectedIndex=initial;voice.SelectedIndex=originalVoice;
                await Layout();
                Check(!Field<bool>("_settingsPending")&&!C<Button>("ApplySettingsButton").IsVisible,"rapid language return left pending state");
            }
            Check(w.FindName("ModNoticeButton") is null&&w.FindName("ModDescriptionText") is null,"removed author button/description remains");
            var settings=Field<UserSettings>("_settings");var originalMod=settings.Mod;
            foreach(var button in new[]{"VanillaGuideButton","ImmortalsGuideButton","ArcaneWarsGuideButton"})
                Check(w.FindName(button) is null,"mod selection still contains an accidental help target");
            var actualProbe=Field<Func<bool>>("_gameRunningProbe");var probes=0;var running=false;
            Set("_gameRunningProbe",new Func<bool>(()=>{probes++;return running;}));
            text.SelectedIndex=1-initial;await Layout();
            Check(probes==1,"language change repeats game process checks");
            text.SelectedIndex=initial;await Layout();
            Check(probes==2&&!Field<bool>("_settingsPending"),"language reversion repeats checks or remains pending");
            text.IsDropDownOpen=true;await Layout();text.IsDropDownOpen=false;await Layout();
            Check(probes==2,"unchanged dropdown close repeats checks");
            running=true;Call("RefreshStatus");
            Check(!C<Button>("LaunchButton").IsEnabled&&!C<Button>("ApplySettingsButton").IsEnabled&&!C<Button>("UpdateButton").IsEnabled,"running game allows an action");
            running=false;Call("RefreshGameLaunchState");
            Check(C<Button>("LaunchButton").IsEnabled,"launch remains blocked after game exits");
            Set("_gameRunningProbe",actualProbe);
            foreach(var mod in new[]{GameMod.Vanilla,GameMod.Immortals})
            {
                settings.Mod=mod;
                var help=(string)Call("CoreHelpText")!;
                Check(help.Contains(language=="ru"?"лимите рот":"company limit"),"negative zero scope missing");
                Check(!help.Contains("Восстанавливает отсутствующие")&&!help.Contains("Restores missing"),"translations advertised as patch fixes");
            }
            settings.Mod=originalMod;Call("SetActivePage","modules");Call("RefreshStatus");await Layout();
            foreach(var size in new[]{(1050,680),(1600,1000)})
            {
                w.Width=size.Item1;w.Height=size.Item2;await Layout();
                var card=C<Border>("ModSelectorCard");var langs=C<Border>("RussianModuleCard");var channel=C<Border>("PatchChannelCard");
                Check(card.ActualHeight<135,"mod card too tall");
                Check(Math.Abs(langs.TranslatePoint(new(),card).Y-channel.TranslatePoint(new(),card).Y)<8,"languages/channel not on same row");
                Check(langs.TranslatePoint(new(langs.ActualWidth,0),card).X<channel.TranslatePoint(new(),card).X,"languages overlap channel");
                Check(channel.TranslatePoint(new(channel.ActualWidth,0),card).X<=card.ActualWidth-10,"channel extends beyond card");
                Save("components-"+size.Item1);
            }
            Call("ShowToast",new Func<string>(()=>language=="ru"?"Настройки сохранены":"Settings saved"),false);await Layout();
            Check(Field<OperationFeedback>("_toast").Remaining?.TotalSeconds is >4 and <=5,"normal toast is not five seconds");
            await Task.Delay(3100);Check(C<Border>("ToastPanel").IsVisible,"toast disappeared at three seconds");
            await Task.Delay(2300);await Layout();Check(!C<Border>("ToastPanel").IsVisible,"toast did not expire after five seconds");
            Call("ShowToast",new Func<string>(()=>"Test error"),true);
            Check(Field<OperationFeedback>("_toast").Remaining?.TotalSeconds is >4 and <=5,"error toast is not five seconds");
            Call("ClearToastStack");
            Console.WriteLine($"COMPACT COMPONENTS PASS {count} {language}: language reversion and single refresh, game running protection, no mod help targets, Discord icon/confirmation, scoped tooltips, two sizes, five-second toast; game state read-only.");
        }
        try{w.Show();var task=w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();var frame=new DispatcherFrame();task.ContinueWith(_=>w.Dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
        finally{w.Close();if(!original.SequenceEqual(File.ReadAllBytes(statePath)))throw new Exception("Read-only game state changed.");}
    }
}
