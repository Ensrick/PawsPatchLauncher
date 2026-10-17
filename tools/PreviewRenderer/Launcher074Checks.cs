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
internal static class Launcher074Checks
{
    internal static void Run(string language,string output)
    {
        if(!ActivityStore.IsSmokeTest)throw new InvalidOperationException("Isolated profile required");
        Directory.CreateDirectory(ActivityStore.Root);Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(ActivityStore.Root,"settings.json"),JsonSerializer.Serialize(new UserSettings {
            PawPatchEnabled=false,CustomPlayerColors=true,DesyncMode="continue",RussianLocalization=true,GameVoiceLanguage="en",ModNoticeSeen=true,Language=language },LauncherJsonContext.Default.UserSettings));
        var config=new LauncherConfiguration{FeedUrls=[],BetaFeedUrls=[],CacheRoot=Path.Combine(ActivityStore.Root,"cache")};
        var w=new MainWindow(config,new FeedClient(config)){Left=-32000,Top=-32000,Width=1050,Height=680,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
        const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
        object? Call(string name,params object?[] values)=>typeof(MainWindow).GetMethod(name,flags)!.Invoke(w,values);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,flags)!.GetValue(w)!;
        void Set(string name,object value)=>typeof(MainWindow).GetField(name,flags)!.SetValue(w,value);
        T C<T>(string name)=>(T)w.FindName(name);
        var n=0;void Check(bool ok,string why){if(!ok)throw new Exception("Launcher 0.7.4: "+why);n++;}
        // Arcane Wars (siege, powers and shards) is not offered in this launcher; its toggles are not checked.
        var toggleNames=new[]{"ColorsToggle","IndependentHostilityToggle","IgnoreDesyncToggle","AdditionalRoamingToggle"};
        void Capture(string name)
        {
            w.UpdateLayout();var view=(FrameworkElement)w.Content;
            var bitmap=new RenderTargetBitmap((int)view.ActualWidth,(int)view.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream=File.Create(Path.Combine(output,name+"-"+language+".png"));encoder.Save(stream);
        }
        async Task Scenario()
        {
            var settings=Field<UserSettings>("_settings");
            Check(!settings.CustomPlayerColors&&!settings.SiegeBalance&&settings.DesyncMode=="official"&&settings.RoamingSpawnMode=="standard","Saved legacy profile retained enabled components");
            var feed=new ChannelManifest { ColorDesyncContinue=true,IndependentColorHostility=true,Packages=new[]{"pawpatch-core","player-colors","roaming-profile-x2-with-new","roaming-profile-x2-no-new"}.Select(id=>new PackageRelease{Id=id,Required=id=="pawpatch-core"}).ToList() };
            Set("_channel",feed);Set("_offeredModChannel",feed);GameMod.SetPawPatch(settings,true);
            settings.CustomPlayerColors=settings.IndependentHostility=settings.AdditionalRoamingCompanies=true;
            settings.DesyncMode="continue";settings.RoamingSpawnMode="x4";
            Call("RefreshModuleAvailability");Call("SetActivePage","modules");Call("RefreshStatus");
            Check(toggleNames.All(name=>C<CheckBox>(name).IsChecked==true),"Enabled fixture did not display selected options");
            C<CheckBox>("PawPatchToggle").IsChecked=false;C<CheckBox>("PawPatchToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(toggleNames.All(name=>C<CheckBox>(name).IsChecked==false&&!C<CheckBox>(name).IsEnabled),"Master off left a checked/clickable component");
            Check(new[]{"StandardSpawnRadio","X2SpawnRadio","X4SpawnRadio"}.All(name=>!C<RadioButton>(name).IsEnabled)&&C<RadioButton>("StandardSpawnRadio").IsChecked==true,"Roaming frequency stayed enabled");
            Check(C<Border>("ColorsModuleCard").Opacity<.5,"Disabled components are not dimmed");
            Check(settings.RussianLocalization&&GameLanguages.Voice(settings)=="en","Master switch changed languages");
            Check(!new SettingsStore().Load().CustomPlayerColors,"Disabled dependent options were not saved");
            await Task.Delay(260);Capture("disabled-components");
            C<CheckBox>("PawPatchToggle").IsChecked=true;C<CheckBox>("PawPatchToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(toggleNames.All(name=>C<CheckBox>(name).IsEnabled&&C<CheckBox>(name).IsChecked==true),"Re-enable did not restore dependent choices");
            C<CheckBox>("PawPatchToggle").IsChecked=false;C<CheckBox>("PawPatchToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var restarted=new SettingsStore().Load();
            var installed=EffectiveSettings.ForChannel(restarted);
            Check(installed.SuspendedArcaneComponents is null&&!installed.CustomPlayerColors,"Applied snapshot leaked remembered choices");
            new SettingsStore().Save(restarted);restarted=new SettingsStore().Load();
            GameMod.SetPawPatch(restarted,true);
            Check(restarted.CustomPlayerColors&&restarted.IndependentHostility&&restarted.AdditionalRoamingCompanies&&restarted.DesyncMode=="continue"&&restarted.RoamingSpawnMode=="x4","Restart after applying disabled configuration lost selection");
            C<CheckBox>("PawPatchToggle").IsChecked=true;C<CheckBox>("PawPatchToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            foreach(var mod in new[]{GameMod.Vanilla,GameMod.Immortals})
            foreach(var channel in new[]{"stable","beta"})
            {
                Set("_installedGameVersion","1.3.72");
                var modVersion=mod==GameMod.Vanilla?"1.3.72":mod==GameMod.Immortals?"2.1":"0.82.1.8";
                var modLabel=GameMod.Name(mod)+" "+modVersion;
                var version=channel=="beta"?"0.3.0-beta.2":"0.1.0";
                var state=new InstallState { AppliedSettings=new(){Mod=mod,Channel=channel,VanillaPawPatchEnabled=true,ImmortalsPawPatchEnabled=true},
                    Modules=new(){[mod==GameMod.ArcaneWars?"pawpatch-core":"pure-fixes-data"]=new(){Enabled=true,Version=version}} };
                state.Modules[mod]=new(){Enabled=true,Version=mod==GameMod.Immortals?"2.1.0":"0.82.1.8-clean.1"};
                Call("RefreshInstalledPatchVersions",state);
                Check(C<TextBlock>("InstalledPatchLabel").Text==(language=="ru"?"УСТАНОВЛЕНО":"INSTALLED")&&C<TextBlock>("InstalledPatchText").Text==modLabel+"\nPaw's Patch "+version,"Installed mod/patch label wrong");
                settings.Mod=mod==GameMod.Vanilla?GameMod.Immortals:GameMod.Vanilla;Call("RefreshInstalledPatchVersions",state);
                Check(C<TextBlock>("InstalledPatchText").Text.StartsWith(modLabel+"\n"),"Draft mode changed installed footer");
                w.UpdateLayout();Check(C<TextBlock>("InstalledPatchText").ActualHeight<=36,"Two-line installed label wrapped to a third line");
                if(mod==GameMod.Immortals&&channel=="beta")Capture("installed-label");
                GameMod.SetPawPatch(state.AppliedSettings,false);Call("RefreshInstalledPatchVersions",state);
                Check(C<TextBlock>("InstalledPatchText").Text==modLabel,"Pure mod still advertises Paw's Patch");
            }
            Call("ClearToastStack");
            void Notify(string text,bool error=true)=>Call("ShowToast",(Func<string>)(()=>text),error);
            var message=language=="ru"?"Нет доступа к папке":"Folder access denied";
            Notify(message);await Task.Delay(260);
            var panel=C<Border>("ToastPanel");var slide=C<TranslateTransform>("ToastSlide");var progress=C<ScaleTransform>("ToastProgressScale");
            Check(C<Border>("ToastCountBadge").Visibility==Visibility.Collapsed,"Single notice shows x1");
            await Task.Delay(150);var before=progress.ScaleX;
            Notify(message);
            Check(C<TextBlock>("ToastCountText").Text=="×2"&&progress.ScaleX<before&&panel.Opacity>.99&&Math.Abs(slide.Y)<.01,"Repeat replayed entrance or failed to renew timer/count");
            Notify(message);Check(C<TextBlock>("ToastCountText").Text=="×3"&&panel.Opacity>.99&&Math.Abs(slide.Y)<.01,"Third repeat replayed card entrance");
            await Task.Delay(230);Capture("toast-count");
            Notify("Independent message",false);Notify(message);
            var stack=C<StackPanel>("ToastStack");var archived=stack.Children.OfType<Border>().First(p=>p!=panel);
            IEnumerable<TextBlock> Texts(DependencyObject view)
            { if(view is TextBlock text)yield return text;for(var i=0;i<VisualTreeHelper.GetChildrenCount(view);i++)foreach(var child in Texts(VisualTreeHelper.GetChild(view,i)))yield return child; }
            Check(stack.Children.Count==2&&Texts(archived).Any(t=>t.Text=="×4"),"Archived repeat lost count or created a duplicate");
            Call("ClearToastStack");Notify(message);await Task.Delay(260);
            Field<OperationFeedback>("_toast").Show(()=>message,true,TimeSpan.FromMilliseconds(30),true);Call("RefreshToast");await Task.Delay(400);
            Check(panel.Visibility==Visibility.Collapsed,"Expired notice did not disappear");
            Notify(message);Check(C<Border>("ToastCountBadge").Visibility==Visibility.Collapsed,"Counter did not reset after expiry");
            await Task.Delay(250);Call("ToastCloseButton_Click",panel,new RoutedEventArgs());await Task.Delay(260);Notify(message);
            Check(C<Border>("ToastCountBadge").Visibility==Visibility.Collapsed,"Counter did not reset after dismissal");
            Console.WriteLine($"LAUNCHER 074 UI PASS {n} {language}: migrated dependency, master toggles, persisted options, six installed labels, stable profile tooltip owner, stationary repeated toasts and count lifecycle.");
        }
        try
        {
            w.Show();var task=w.Dispatcher.InvokeAsync(Scenario).Task.Unwrap();var frame=new DispatcherFrame();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(30)};timer.Tick+=(_,_)=>frame.Continue=false;
            task.ContinueWith(_=>w.Dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
            timer.Start();try{Dispatcher.PushFrame(frame);}finally{timer.Stop();}
            if(!task.IsCompleted)throw new TimeoutException();task.GetAwaiter().GetResult();
        }
        finally{Set("_busy",false);w.Close();}
    }
}
