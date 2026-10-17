using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class ModePerformanceChecks
{
    public static void Run(string configPath,string game)
    {
        if(!ActivityStore.IsSmokeTest)throw new Exception("Isolated profile required");
        var original=File.ReadAllBytes(Path.Combine(game,".pawpatch/state.json"));
        var config=JsonSerializer.Deserialize(File.ReadAllText(configPath),LauncherJsonContext.Default.LauncherConfiguration)!;
        var client=new FeedClient(config);var feed=Task.Run(()=>client.GetChannelAsync()).GetAwaiter().GetResult()!;
        new SettingsStore().Save(new UserSettings { GamePath=game,ModNoticeSeen=true,Mod=GameMod.Vanilla });
        var w=new MainWindow(config,client){Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual,Width=1440,Height=900};
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        void Set(string n,object? v)=>typeof(MainWindow).GetField(n,flags)!.SetValue(w,v);
        object? Call(string n,params object?[] a)=>typeof(MainWindow).GetMethod(n,flags)!.Invoke(w,a);
        async Task Scenario()
        {
            Set("_game",new GameInstallation(game,Path.Combine(game,"k2.exe"),"test","stable"));Set("_gameRunningProbe",new Func<bool>(()=>false));
            Set("_channel",feed);Set("_offeredModChannel",feed);Set("_latestChannel",feed);
            Call("SetActivePage","modules");Call("RefreshStatus");await Task.Delay(400);
            var timings=new List<double>();
            foreach(var name in Enumerable.Range(0,4).SelectMany(_=>new[]{"ImmortalsModRadio","VanillaModRadio"}))
            {
                var watch=Stopwatch.StartNew();((RadioButton)w.FindName(name)).IsChecked=true;w.UpdateLayout();watch.Stop();timings.Add(watch.Elapsed.TotalMilliseconds);
                var selected=(UserSettings)typeof(MainWindow).GetField("_settings",flags)!.GetValue(w)!;
                if(selected.Mod!=(string)((RadioButton)w.FindName(name)).Tag)throw new Exception("Measured selection was rejected: "+name);
                await (Task<bool>)Call("CheckGameCompatibilityAsync",true)!;
                await w.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ContextIdle);
                if(watch.Elapsed.TotalMilliseconds>500)throw new Exception("Mode selection blocked a frame for more than 500ms");
                Console.WriteLine($"MODE SWITCH {name}: {watch.Elapsed.TotalMilliseconds:F1} ms (including layout; verification awaited separately)");
                Call("CloseCompatibilityPopup");await Task.Delay(200);
            }
            if(!File.ReadAllBytes(Path.Combine(game,".pawpatch/state.json")).SequenceEqual(original))throw new Exception("Mode selection changed applied files");
            Console.WriteLine($"MODE PERFORMANCE PASS {timings.Count}: median={timings.Order().ElementAt(timings.Count/2):F1} ms; max={timings.Max():F1} ms; applied state unchanged");
            var names=new[]{"ImmortalsModRadio","VanillaModRadio"};
            for(var burst=0;burst<3;burst++)
            {
                var probes=new List<Task<bool>>();var unfinished=new List<Task<bool>>();
                for(var i=0;i<15;i++)
                {
                    if(probes.LastOrDefault() is {IsCompleted:false} pending)unfinished.Add(pending);
                    ((RadioButton)w.FindName(names[(i+burst)%3])).IsChecked=true;
                    probes.Add((Task<bool>)Call("CheckGameCompatibilityAsync",true)!);
                }
                await Task.WhenAll(probes);w.UpdateLayout();
                var finalName=names[(14+burst)%3];var finalMod=(string)((RadioButton)w.FindName(finalName)).Tag;
                var selected=(UserSettings)typeof(MainWindow).GetField("_settings",flags)!.GetValue(w)!;
                var identity=(string)typeof(MainWindow).GetField("_compatibilityKey",flags)!.GetValue(w)!;
                if(selected.Mod!=finalMod||!identity.StartsWith(finalMod+"|")||!probes[^1].Result||unfinished.Any(p=>p.Result))
                    throw new Exception("A stale compatibility check overrode the final rapid selection");
                if(unfinished.Count<10)throw new Exception("Rapid test did not overlap enough background checks");
                if(!File.ReadAllBytes(Path.Combine(game,".pawpatch/state.json")).SequenceEqual(original))throw new Exception("Rapid selection changed installed files");
                Console.WriteLine($"RAPID SWITCH PASS 15: final={finalMod}; stale={unfinished.Count}; final probe accepted; applied state unchanged");
            }
        }
        try
        {
            w.Show();var task=w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();var frame=new DispatcherFrame();task.ContinueWith(_=>w.Dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)));Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
        }
        finally {w.Close();}
    }
}
