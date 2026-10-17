using PawsPatchLauncher;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace PreviewRenderer;

internal static class ModModeChecks
{
    internal static void Run(string language)
    {
        if (!ActivityStore.IsSmokeTest) throw new Exception("Smoke isolation required");
        var w = new MainWindow(new LauncherConfiguration { FeedUrls = [], BetaFeedUrls = [] }, null) {
            Left=-30000,Top=-30000,ShowActivated=false,ShowInTaskbar=false,WindowStartupLocation=WindowStartupLocation.Manual };
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, flags)!.Invoke(w, args);
        void Set(string name,object value) => typeof(MainWindow).GetField(name,flags)!.SetValue(w,value);
        T Control<T>(string name) => (T)w.FindName(name);
        var settings=(UserSettings)typeof(MainWindow).GetField("_settings",flags)!.GetValue(w)!;
        ConfigurationCode.Apply(new UserSettings(),settings);
        ((PawsPatchLauncher.Localization)typeof(MainWindow).GetField("_text",flags)!.GetValue(w)!).SetLanguage(language);
        var checks=0;
        void Check(bool ok,string reason) { checks++;if(!ok)throw new Exception(reason); }
        try
        {
            Call("ApplyLanguage"); Call("SetActivePage","modules");
            w.Show(); w.UpdateLayout();
            var vanilla=Control<RadioButton>("VanillaModRadio");var immortals=Control<RadioButton>("ImmortalsModRadio");
            immortals.IsChecked=true;
            Check(settings.Mod==GameMod.Immortals&&vanilla.IsEnabled,"Initial mode/Vanilla");
            Check(Control<TextBlock>("CoreTitleText").Text=="Paw's Patch","Core title");
            Call("RefreshModControls");
            Check(Control<Border>("ModSelectorCard").TranslatePoint(new Point(), w).Y<Control<Border>("VanillaEmptyCard").TranslatePoint(new Point(), w).Y,"Tabs are not above components");
            settings.RussianLocalization=true;
            vanilla.IsChecked=true;
            Check(settings.Mod==GameMod.Vanilla && Control<Border>("VanillaEmptyCard").Visibility==Visibility.Visible,"Vanilla tab inactive");
            foreach(var name in new[]{"CoreModuleCard","ColorsModuleCard","OosModuleCard","IndependentHostilityCard","RoamingSpawnCard","AdditionalRoamingCard","SiegeBalanceCard","PowersShardsCard"})
                Check(Control<Border>(name).Visibility==Visibility.Collapsed,"Vanilla leaked component "+name);
            Check(Control<Border>("RussianModuleCard").IsVisible,"Vanilla lost its shared language selection");
            immortals.IsChecked=true;
            Check(settings.Mod==GameMod.Immortals&&settings.RussianLocalization,"Switching games changed the shared language");
            Set("_busy",true);Call("RefreshStatus");
            Check(!vanilla.IsEnabled&&!immortals.IsEnabled,"Mode changed during install");
            Set("_busy",false);Call("RefreshStatus");
            Call("SetActivePage","home");
            Check(Control<Border>("ModSelectorCard").Visibility==Visibility.Collapsed,"Tabs leaked onto Home");
            Call("SetActivePage","modules");
            // The game is chosen in the Mod manager; Components names it and links there.
            var title=Control<TextBlock>("ModulesTitleText");var change=Control<Button>("ComponentsChangeGameButton");
            Check(Control<Grid>("ModChoiceGrid").Visibility==Visibility.Collapsed,"Game choice shown on Components");
            Check(title.Text.EndsWith(GameMod.Name(settings.Mod,language=="ru")),"Components header does not name the game");
            foreach(var size in new[]{(1050d,680d),(1280d,800d),(1600d,1000d)})
            {
                w.Width=size.Item1;w.Height=size.Item2;w.Measure(new Size(w.Width,w.Height));w.Arrange(new Rect(0,0,w.Width,w.Height));w.UpdateLayout();
                Check(title.ActualWidth>100&&change.IsVisible&&change.ActualWidth>60,"Components header clipped at "+size);
            }
            change.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check(Control<StackPanel>("ModManagerPanel").IsVisible&&!Control<Border>("CoreModuleCard").IsVisible,"Change game does not open the Mod manager");
            Console.WriteLine($"MOD UI PASS {checks} {language}: game header and Mod manager link, empty state, preserved language, help channel scope, busy lock, three widths.");
        }
        finally { Set("_busy",false);ConfigurationCode.Apply(new UserSettings(),settings);new SettingsStore().Save(settings);w.Close(); }
    }
}
