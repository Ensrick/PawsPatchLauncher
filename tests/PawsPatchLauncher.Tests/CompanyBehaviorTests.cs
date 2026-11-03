using PawsPatchLauncher;
using System.Text.Json;
internal static class CompanyBehaviorTests
{
    internal static void Run()
    {
        int count=0;
        void Check(bool b){count++;if(!b)throw new Exception("Company behavior check "+count);}
        Check(!new UserSettings().IgnoreZeroMorale);
        Check(!JsonSerializer.Deserialize("{}",LauncherJsonContext.Default.UserSettings)!.IgnoreZeroMorale);
        foreach(var mod in new[]{GameMod.ArcaneWars,GameMod.Vanilla,GameMod.Immortals})
        foreach(var channel in new[]{"stable","beta"})
        foreach(var core in new[]{false,true})
        foreach(var data in new[]{false,true})
        foreach(var enabled in new[]{false,true})
        {
            var s=new UserSettings{Mod=mod,Channel=channel,DataOnly=data};
            GameMod.SetPawPatch(s,core);s.IgnoreZeroMorale=enabled;
            bool expected=mod==GameMod.ArcaneWars&&core&&!data&&enabled;
            var active=EffectiveSettings.ForChannel(s);Check(active.IgnoreZeroMorale==expected);
            var code=ConfigurationCode.Create(s);Check(ConfigurationCode.Parse(code).IgnoreZeroMorale==expected);
            var feed=new ChannelManifest{Channel=channel};Check(!EffectiveSettings.ForFeed(s,feed).IgnoreZeroMorale);
            feed.CompanyBehaviorRevision=1;Check(EffectiveSettings.ForFeed(s,feed).IgnoreZeroMorale==expected);
            feed.Channel=channel=="beta"?"stable":"beta";Check(!EffectiveSettings.ForFeed(s,feed).IgnoreZeroMorale);
            var clone=JsonSerializer.Deserialize(JsonSerializer.Serialize(active,LauncherJsonContext.Default.UserSettings),LauncherJsonContext.Default.UserSettings)!;Check(clone.IgnoreZeroMorale==expected);
        }
        var original=new UserSettings{IgnoreZeroMorale=true};GameMod.SetPawPatch(original,false);Check(!original.IgnoreZeroMorale);GameMod.SetPawPatch(original,true);Check(original.IgnoreZeroMorale);
        var codeOn=ConfigurationCode.Create(original);var target=new UserSettings();ConfigurationCode.Apply(ConfigurationCode.Parse(codeOn),target);Check(target.IgnoreZeroMorale);
        Check(ConfigurationChanges.Compare(new UserSettings(),original,"ru").Any(c=>c.Name=="Игнорировать нулевой боевой дух"));
        foreach(var lang in new[]{"ru","en"})foreach(var key in new[]{"modules.exhaustion","modules.exhaustion.desc","modules.exhaustion.help"})Check(new Localization(lang)[key]!=key);
        Console.WriteLine("COMPANY_BEHAVIOR_LAUNCHER_PASS "+count);
    }
}
