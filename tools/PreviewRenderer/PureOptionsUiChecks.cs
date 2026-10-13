using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using PawsPatchLauncher;

namespace PreviewRenderer;
internal static class PureOptionsUiChecks
{
    internal static void Run(string configPath)
    {
        if (!ActivityStore.IsSmokeTest) throw new Exception("Disposable profile required");
        var config=JsonSerializer.Deserialize(File.ReadAllText(configPath),LauncherJsonContext.Default.LauncherConfiguration)!;
        var client=new FeedClient(config);
        var feeds=new[]{"stable","beta"}.Select(c=>Task.Run(()=>client.GetChannelAsync(c)).GetAwaiter().GetResult()!).ToArray();
        int count=0;
        void Check(bool ok,string why){count++;if(!ok)throw new Exception(why);}
        foreach(var language in new[]{"ru","en","de","fr","cs","uk"})
        {
            new SettingsStore().Save(new UserSettings{Language=language,ModNoticeSeen=true});
            var w=new MainWindow(new LauncherConfiguration{FeedUrls=[],BetaFeedUrls=[]},null){Left=-32000,Top=-32000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual};
            const BindingFlags f=BindingFlags.Instance|BindingFlags.NonPublic;
            void Set(string n,object? v)=>typeof(MainWindow).GetField(n,f)!.SetValue(w,v);
            object? Call(string n,params object[] a)=>typeof(MainWindow).GetMethod(n,f)!.Invoke(w,a);
            T C<T>(string n)=>(T)w.FindName(n);
            var prefs=(UserSettings)typeof(MainWindow).GetField("_settings",f)!.GetValue(w)!;
            var loc=(PawsPatchLauncher.Localization)typeof(MainWindow).GetField("_text",f)!.GetValue(w)!;
            loc.SetLanguage(language);
            try
            {
                foreach(var mod in new[]{GameMod.Vanilla,GameMod.Immortals})
                foreach(var feed in feeds)
                {
                    prefs.Mod=mod;prefs.Channel=feed.Channel;
                    GameMod.SetPawPatch(prefs,true);GameMod.SetColors(prefs,true);GameMod.SetDesync(prefs,true);
                    Set("_channel",feed);Set("_latestChannel",feed);Set("_offeredModChannel",feed);Set("_colorsAvailable",true);
                    Set("_compatibilityState",GameCompatibilityState.Supported);
                    Call("ApplyLanguage");Call("SetActivePage","modules");Call("RefreshModControls");
                    bool beta=GameMod.HasPureOptions(feed), extended=GameMod.HasPureBeta(feed);
                    foreach(var card in new[]{"ColorsModuleCard","OosModuleCard"})Check(C<Border>(card).Visibility==(beta?Visibility.Visible:Visibility.Collapsed),mod+feed.Channel+card);
                    foreach(var card in new[]{"IndependentHostilityCard","RoamingSpawnCard","AdditionalRoamingCard"})Check(C<Border>(card).Visibility==(extended?Visibility.Visible:Visibility.Collapsed),"Pure card visibility "+card);
                    Check(C<Border>("PureLairCard").Visibility==(extended&&!feed.PureLairRecoveryBuiltIn?Visibility.Visible:Visibility.Collapsed),"Core defenders have no separate toggle");
                    Check(C<Border>("SiegeBalanceCard").Visibility==Visibility.Collapsed,"Siege balance leaked");
                    Check(C<Border>("ImprovedAiCard").Visibility==(extended?Visibility.Visible:Visibility.Collapsed),"Improved AI visibility");
                    var help=(string)Call("CoreHelpText")!;
                    Check(help.Length>200&&(extended||help.Contains("Dvorak")),"Missing core help "+language);
                    if(language is not ("ru" or "en"))Check(!help.Contains("Faster native saved-game transfers")&&!help.Contains("Optional Beta switches"),"Untranslated core help "+language);
                    if(!beta)continue;
                    if (extended && language == "ru")
                    {
                        var content=(FrameworkElement)w.Content;
                        content.Width=1600;content.Height=1000;
                        content.Measure(new Size(1600,1000));content.Arrange(new Rect(0,0,1600,1000));content.UpdateLayout();
                        var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1600,1000,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(content);
                        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var image=File.Create(Path.Combine(Path.GetDirectoryName(configPath)!,"pure-options-"+mod+"-ru.png"));encoder.Save(image);
                    }
                    var colors=C<CheckBox>("ColorsToggle");var sync=C<CheckBox>("IgnoreDesyncToggle");var core=C<CheckBox>("PawPatchToggle");
                    Check(colors.IsChecked==true&&sync.IsChecked==true&&colors.IsEnabled&&sync.IsEnabled,"Beta on controls "+mod);
                    core.IsChecked=false;Call("PawPatchChanged",core,new RoutedEventArgs());
                    Check(colors.IsChecked==false&&sync.IsChecked==false&&!colors.IsEnabled&&!sync.IsEnabled,"Master off left controls active");
                    core.IsChecked=true;Call("PawPatchChanged",core,new RoutedEventArgs());
                    Check(colors.IsChecked==true&&sync.IsChecked==true,"Master on lost mod choices");
                    if (extended)
                    {
                        var lair=C<CheckBox>("PureLairToggle");var ai=C<CheckBox>("ImprovedAiToggle");
                        Check(ai.IsEnabled,"Pure AI option disabled");
                        if (feed.PureLairRecoveryBuiltIn)
                            Check(GameMod.PureComponents(EffectiveSettings.ForFeed(prefs,feed)).WoundedLairDefenders,"Core defenders inactive");
                        else
                        {
                            Check(lair.IsEnabled,"Legacy lair option disabled");
                            lair.IsChecked=false;Call("GameplayOptionChanged",lair,new RoutedEventArgs());
                            Check(!GameMod.PureComponents(prefs).WoundedLairDefenders,"Lair choice not saved");
                            lair.IsChecked=true;Call("GameplayOptionChanged",lair,new RoutedEventArgs());
                        }
                    }
                    Set("_compatibilityState",GameCompatibilityState.Unsupported);Call("RefreshPawComponentDependency");
                    Check(colors.IsChecked==false&&sync.IsChecked==false&&!colors.IsEnabled&&!sync.IsEnabled,"Unsupported EXE controls active");
                }
            }
            finally{w.Close();}
        }
        Console.WriteLine($"PURE OPTIONS UI PASS {count}: two modes, both channels, all six languages, master switch, unsupported executable");
    }
}
