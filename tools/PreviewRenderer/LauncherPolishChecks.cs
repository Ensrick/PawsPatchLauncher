using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class LauncherPolishChecks
{
    internal static void Run(string language, string output, string configurationPath)
    {
        if (!ActivityStore.IsSmokeTest) throw new Exception("Disposable profile required.");
        Directory.CreateDirectory(output);
        var config=JsonSerializer.Deserialize(File.ReadAllText(configurationPath),LauncherJsonContext.Default.LauncherConfiguration)!;
        // Copy signed metadata for a realistic history; tests never write the live cache.
        var source=config.CacheRoot!;config.CacheRoot=Path.Combine(ActivityStore.Root,"polish-cache");
        Directory.CreateDirectory(Path.Combine(config.CacheRoot,"releases"));
        foreach(var file in Directory.GetFiles(Path.Combine(source,"releases"),"*.json"))File.Copy(file,Path.Combine(config.CacheRoot,"releases",Path.GetFileName(file)),true);
        var client=new FeedClient(config);
        var feed=Task.Run(()=>client.GetChannelAsync()).GetAwaiter().GetResult()!;
        _=Task.Run(()=>client.GetChannelAsync("beta")).GetAwaiter().GetResult();
        new SettingsStore().Save(new(){Mod=GameMod.Immortals,Language=language,ModNoticeSeen=true,RussianLocalization=true,GameVoiceLanguage="en"});
        var w=new MainWindow(config,client){Left=-32000,Top=-32000,Width=1600,Height=1000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        T Field<T>(string n)=>(T)typeof(MainWindow).GetField(n,flags)!.GetValue(w)!;
        void Set(string n,object? v)=>typeof(MainWindow).GetField(n,flags)!.SetValue(w,v);
        object? Call(string n,params object?[] a)=>typeof(MainWindow).GetMethod(n,flags)!.Invoke(w,a);
        T C<T>(string n)=>(T)w.FindName(n);
        var checks=0;void Check(bool ok,string reason){if(!ok)throw new Exception("Launcher polish: "+reason);checks++;}
        async Task Layout(){await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);w.UpdateLayout();}
        void Capture(string name){var image=new RenderTargetBitmap((int)w.ActualWidth,(int)w.ActualHeight,96,96,PixelFormats.Pbgra32);image.Render(w);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(image));using var stream=File.Create(Path.Combine(output,name+"-"+language+".png"));encoder.Save(stream);}
        async Task Scenario()
        {
            Set("_channel",feed);Set("_offeredModChannel",feed);Set("_latestChannel",feed);Set("_game",null);
            var settings=Field<UserSettings>("_settings");Call("ApplyLanguage");Call("SetActivePage","modules");Call("RefreshStatus");await Layout();
            Check(C<Border>("PatchChannelCard").IsVisible&&Math.Abs(C<Border>("PatchChannelCard").TranslatePoint(new(),w).Y-C<Border>("RussianModuleCard").TranslatePoint(new(),w).Y)<8,"channel is not beside languages");
            Check(C<Border>("RussianModuleCard").ActualHeight<66,"language row too tall");
            Check(C<ComboBox>("GameVoiceCombo").IsEnabled,"separate speech disabled");
            var state=new InstallState{AppliedSettings=new(){Mod=GameMod.Immortals,ImmortalsPawPatchEnabled=true},Modules=new(){["pure-fixes-data"]=new(){Enabled=true,Version="1.0.0-pure.1"}}};
            Call("RefreshInstalledPatchVersions",state);var installed=C<TextBlock>("InstalledPatchText").Text;
            foreach(var mod in new[]{GameMod.Vanilla,GameMod.Immortals}){settings.Mod=mod;Call("RefreshInstalledPatchVersions",state);Check(C<TextBlock>("InstalledPatchText").Text==installed,"installed footer changed with draft mode");}
            settings.Mod=GameMod.Immortals;Call("RefreshStatus");
            var timings=new List<double>();
            for(var i=0;i<8;i++){var radio=C<RadioButton>(i%2==0?"VanillaModRadio":"ImmortalsModRadio");var watch=Stopwatch.StartNew();radio.IsChecked=true;w.UpdateLayout();timings.Add(watch.Elapsed.TotalMilliseconds);await Layout();}
            File.WriteAllText(Path.Combine(output,"selection-timing-"+language+".json"),JsonSerializer.Serialize(timings));
            Check(timings.Skip(2).Max()<500,"warm mode selection blocked the UI for half a second");
            foreach(var size in new[]{(1600,1000),(1050,680)})
            {
                w.Width=size.Item1;w.Height=size.Item2;await Layout();Capture("components-"+size.Item1);
                Call("SetBusy",true,"Проверка загрузки / Checking download");await Layout();var panel=C<Border>("OperationStatusPanel");var height=panel.ActualHeight;
                Check(height<106&&height>=85,"progress is too tall");
                Call("SetTransferDetails","97 MB / 100 MB · 12 MB/s · 1 s",false);C<Button>("CancelDownloadButton").Visibility=Visibility.Visible;await Layout();
                Check(Math.Abs(panel.ActualHeight-height)<1,"transfer text resized progress");Capture("progress-"+size.Item1);
                C<Button>("CancelDownloadButton").Visibility=Visibility.Collapsed;Call("SetTransferDetails","Проверка / Verification",true);await Layout();
                Check(Math.Abs(panel.ActualHeight-height)<1,"verification resized progress");Call("SetBusy",false,null);await Layout();
            }
            Console.WriteLine($"LAUNCHER POLISH PASS {checks} {language}; mode selection ms: {string.Join(", ",timings.Select(t=>t.ToString("F1")))}; mocked external opening only.");
        }
        try{w.Show();var task=w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();var frame=new DispatcherFrame();task.ContinueWith(_=>w.Dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);var timeout=new DispatcherTimer{Interval=TimeSpan.FromSeconds(55)};timeout.Tick+=(_,_)=>frame.Continue=false;timeout.Start();Dispatcher.PushFrame(frame);timeout.Stop();if(!task.IsCompleted)throw new TimeoutException("Polish UI checks timed out");task.GetAwaiter().GetResult();}
        finally{Set("_busy",false);w.Close();}
    }
}
