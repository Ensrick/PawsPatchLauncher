using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PawsPatchLauncher;

namespace PreviewRenderer;

internal static class PerModChannelChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new InvalidOperationException("Disposable smoke profile required.");
        var root = Path.Combine(ActivityStore.Root, "per-mod-channels"); Directory.CreateDirectory(root);
        ChannelManifest Feed(string branch) => new()
        {
            Channel = branch, Packages = [new() { Id = "immortals", Version = "2.1", Size = 1, Sha256 = new string('C',64) },
                new() { Id = "pure-fixes-data", Version = "1", Size = 1, Sha256 = new string('D',64) },
                new() { Id = "pure-fixes-runtime", Version = branch == "beta" ? "2-beta.1" : "1", Size = 1, Sha256 = new string('E',64) }]
        };
        var stable = Feed("stable"); var beta = Feed("beta");
        var config = new LauncherConfiguration { FeedUrls = [Path.Combine(root,"stable.json")], BetaFeedUrls = [Path.Combine(root,"beta.json")], CacheRoot = Path.Combine(root,"cache") };
        void SaveFeeds()
        {
            File.WriteAllText(config.FeedUrls[0],JsonSerializer.Serialize(stable,LauncherJsonContext.Default.ChannelManifest));
            File.WriteAllText(config.BetaFeedUrls[0],JsonSerializer.Serialize(beta,LauncherJsonContext.Default.ChannelManifest));
        }
        SaveFeeds();
        var profile = new UserSettings { Mod = GameMod.Immortals, ModNoticeSeen = true, RussianLocalization = true, Language = language };
        new SettingsStore().Save(profile);
        var window = new MainWindow(config, null) { Width = 1050, Height = 780, Left = -32000, Top = -32000,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object?[] args) => typeof(MainWindow).GetMethod(name,flags)!.Invoke(window,args);
        T Field<T>(string name) => (T)typeof(MainWindow).GetField(name,flags)!.GetValue(window)!;
        T Control<T>(string name) => (T)window.FindName(name);
        typeof(MainWindow).GetField("_game",flags)!.SetValue(window,null);
        var settings = Field<UserSettings>("_settings");
        Field<PawsPatchLauncher.Localization>("_text").SetLanguage(language);
        Call("ApplyLanguage"); Call("SetActivePage","modules"); window.Show(); window.UpdateLayout();
        int count = 0;
        void Check(bool ok,string why) { count++; if (!ok) throw new InvalidOperationException("Per-mod channel UI: " + why); }
        async Task Settled()
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);
            while ((Field<bool>("_busy") || Field<bool>("_checkingFeed")) && DateTimeOffset.UtcNow < deadline) await Task.Delay(25);
            await Task.Delay(180); window.UpdateLayout();
            Check(!Field<bool>("_busy") && !Field<bool>("_checkingFeed"),"Channel check did not settle");
        }
        async Task SelectMod(string name)
        {
            Check(Control<RadioButton>(name).IsEnabled,"Mod selector is disabled");
            Control<RadioButton>(name).IsChecked = true; await Settled();
        }
        async Task Scenario()
        {
            await (Task<bool>)Call("CheckFeedAsync",false)!;
            Check(window.FindName("HeaderReleaseRadio") is null && window.FindName("SettingsBetaRadio") is null,"Global selectors remain");
            Check(Control<RadioButton>("ModBetaRadio").IsEnabled,"Available Immortals beta is hidden");
            Check(Control<RadioButton>("ModBetaRadio").IsVisible,"Selector is outside the visible Paw card");
            Control<RadioButton>("ModBetaRadio").IsChecked = true; await Settled();
            Check(settings.Channel == "beta","Beta click did not change the Immortals selection");
            Check(Control<FrameworkElement>("HelpOverlay").Visibility == Visibility.Visible,"First beta notice missing");
            Check(Control<TextBlock>("HelpTitleText").Text.Contains("Immortals"),"Notice refers to another mod");
            await (Task)Call("CloseHelpAsync")!;
            Check(settings.BetaNoticesSeen.Contains(GameMod.Immortals),"Beta notice not remembered");
            await SelectMod("VanillaModRadio");
            Check(settings.Mod == GameMod.Vanilla && settings.Channel == "stable","Immortals beta leaked into Vanilla");
            Check(Control<RadioButton>("ModReleaseRadio").IsChecked == true,"Vanilla does not show its own release choice");
            await SelectMod("ImmortalsModRadio");
            Check(settings.Channel == "beta" && Control<RadioButton>("ModBetaRadio").IsChecked == true,"Immortals did not remember beta");
            Check(Control<FrameworkElement>("HelpOverlay").Visibility != Visibility.Visible,"Beta notice repeats on mod return");
            Check(settings.RussianLocalization,"Mod/channel change reset game language");
            settings.PinnedRelease="keep-this-pin";
            await (Task)Call("ChangeChannelAsync",true)!;
            Check(settings.PinnedRelease=="keep-this-pin","Re-click discarded pin"); settings.PinnedRelease=null;
            await SelectMod("VanillaModRadio");
            Check(settings.Channel=="stable","The Immortals beta choice changed Vanilla");
            var restored=new SettingsStore().Load(); ModChannelSelection.SelectMod(restored,GameMod.Immortals);
            Check(restored.Channel=="beta","Immortals choice lost on restart");
            ModChannelSelection.SelectMod(restored,GameMod.Vanilla);
            Check(restored.Channel=="stable","Vanilla choice lost on restart");
            Check(!Directory.Exists(Path.Combine(root,".pawpatch")),"Selecting channels installed game files");
            // Help follows the selected mod's signed guide, including historical
            // versions; native-only features must be listed as unavailable in
            // data-only compatibility mode while the hotkey file still works.
            var dvorak=new PatchGuideEntry("dvorak","always","","","","");
            var transfer=new PatchGuideEntry("fast-save-transfer","beta","","","","");
            // A cached installation may use the pre-Dvorak catalog, but help
            // follows the selected current channel without applying anything.
            var nativeText=language=="ru"?"передача сохранений":"saved-game transfers";
            foreach(var branch in new[]{"stable","beta"})
            foreach(var mod in new[]{GameMod.Vanilla,GameMod.Immortals})
            {
                settings.Channel=branch;settings.Mod=mod;
                var current=Feed(branch);current.ModGuides=[new() {Id=mod,PatchGuide=new() {Entries=branch=="beta"?[dvorak,transfer]:[dvorak]}}];
                typeof(MainWindow).GetField("_channel",flags)!.SetValue(window,Feed(branch));
                typeof(MainWindow).GetField("_latestChannel",flags)!.SetValue(window,current);
                Call("RefreshCompatibilityControls");
                var tooltip=(string)Control<Button>("CoreHelpButton").ToolTip;
                Check(tooltip.Contains("Dvorak")&&tooltip.Contains(nativeText)==(branch=="beta"),"Hover tooltip ignores selected mod/channel features: "+mod+" "+branch);
                Control<Button>("CoreHelpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(Control<TextBlock>("HelpBodyText").Text==tooltip,"Clicked help and hover tooltip disagree");
                await (Task)Call("CloseHelpAsync")!;
            }
            // The actual frequency control must follow current signed channel
            // text, but retain old values when an archive is explicitly pinned.
            foreach(var branch in new[]{"stable","beta"})
            {
                settings.Mod=GameMod.Immortals;settings.Channel=branch;
                var old=Feed(branch);var current=Feed(branch);
                old.ModGuides=[new() { Id=GameMod.Immortals,PatchGuide=new() { Entries=[new("frequency","optional","","","Старый ×4: 240 / 40%","Old ×4: 240 / 40%")] } }];
                current.ModGuides=[new() { Id=GameMod.Immortals,PatchGuide=new() { Entries=[new("frequency","optional","","","Тёмный лорд ×4: 6 минут, 30%","Shadow Lord ×4: 6 minutes, 30%")] } }];
                typeof(MainWindow).GetField("_channel",flags)!.SetValue(window,old);
                typeof(MainWindow).GetField("_latestChannel",flags)!.SetValue(window,current);
                foreach(var pinned in new[]{false,true})
                {
                    settings.PinnedRelease=pinned?ChannelFingerprint.Create(old):null;
                    Call("RefreshCompatibilityControls");
                    var expected=(pinned?old:current).ModGuides[0].PatchGuide!.Entries[0].Body(language);
                    var tooltip=(string)Control<Button>("SpawnHelpButton").ToolTip;
                    Check(tooltip==expected,"Frequency tooltip ignores current/archive channel: "+branch+" "+pinned);
                    Control<Button>("SpawnHelpButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check(Control<TextBlock>("HelpBodyText").Text==tooltip,"Frequency hover and clicked help disagree");
                    await (Task)Call("CloseHelpAsync")!;
                }
                settings.PinnedRelease=null;
            }
            settings.Channel="beta";
            typeof(MainWindow).GetField("_latestChannel",flags)!.SetValue(window,null);
            var selectedGuide=new PatchGuideDocument { Entries=[dvorak,transfer] };
            beta.ModGuides=[new() { Id=GameMod.Vanilla,PatchGuide=selectedGuide },
                new() { Id=GameMod.Immortals,PatchGuide=new() { Entries=[dvorak] } }];
            typeof(MainWindow).GetField("_channel",flags)!.SetValue(window,beta);
            settings.Mod=GameMod.Vanilla;
            string Help(bool partial)=>(string)Call("PureFixesDescription",partial)!;
            Check(Help(false).Contains("Dvorak")&&Help(false).Contains(nativeText),"Pure beta features missing from help");
            var partial=Help(true);var unavailable=partial.IndexOf(language=="ru"?"Недоступны":"Unavailable",StringComparison.Ordinal);
            Check(partial.IndexOf("Dvorak",StringComparison.Ordinal)<unavailable&&partial.IndexOf(nativeText,StringComparison.Ordinal)>unavailable,
                "Native transfer advertised as available for an incompatible executable");
            settings.Mod=GameMod.Immortals;
            Check(Help(false).Contains("Dvorak")&&!Help(false).Contains(nativeText),"Another mod's beta transfer leaked into stable help");
            selectedGuide.Entries.Clear();settings.Mod=GameMod.Vanilla;
            Check(!Help(false).Contains("Dvorak")&&!Help(false).Contains(nativeText),"Historical patch advertises newer features");
            settings.PinnedRelease=ChannelFingerprint.Create(beta);
            var newer=Feed("beta");newer.ModGuides=[new() {Id=GameMod.Vanilla,PatchGuide=new() {Entries=[dvorak,transfer]}}];
            typeof(MainWindow).GetField("_latestChannel",flags)!.SetValue(window,newer);
            Call("RefreshCompatibilityControls");
            Check(!((string)Control<Button>("CoreHelpButton").ToolTip).Contains("Dvorak"),"Pinned historical tooltip advertises latest features");
            settings.PinnedRelease=null;
            typeof(MainWindow).GetField("_latestChannel",flags)!.SetValue(window,null);
            Console.WriteLine($"PER-MOD CHANNEL UI PASS {count} {language}: actual controls, independent choices, first notice, pins, restart and language; local feeds only");
        }
        try
        {
            var task=window.Dispatcher.InvokeAsync(Scenario).Task.Unwrap(); var frame=new DispatcherFrame();
            var timeout=new DispatcherTimer { Interval=TimeSpan.FromSeconds(40) }; timeout.Tick+=(_,_)=>frame.Continue=false;
            task.ContinueWith(_=>window.Dispatcher.BeginInvoke(()=>frame.Continue=false),TaskScheduler.Default);
            timeout.Start();try { Dispatcher.PushFrame(frame); } finally { timeout.Stop(); }
            if(!task.IsCompleted) throw new TimeoutException("Per-mod channel UI test timed out."); task.GetAwaiter().GetResult();
        }
        finally { window.Close(); }
    }
}
