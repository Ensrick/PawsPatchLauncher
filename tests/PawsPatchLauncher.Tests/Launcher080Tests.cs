using System.Text.Json;
using System.Text.RegularExpressions;
using PawsPatchLauncher;

public static class Launcher080Tests
{
    public static int Run()
    {
        var checks=0;
        void Check(bool value,string why){checks++;if(!value)throw new Exception("Launcher 0.8: "+why);}
        Check(UiLanguages.Choices.Select(c=>c.Label).SequenceEqual(new[]{"English","Русский","Українська","Čeština","Deutsch","Français"}),"autonyms");
        Check(UiLanguages.Normalize("CS")=="cs"&&UiLanguages.Normalize("ja")=="en","language normalization");
        foreach(var code in new[]{"uk","cs","de","fr"})
        {
            using var stream=typeof(UiLanguages).Assembly.GetManifestResourceStream("PawsPatchLauncher.Assets.Languages."+code+".json");
            Check(stream is not null,"embedded language catalog");
            var catalog=JsonSerializer.Deserialize<Dictionary<string,string>>(stream!)!;Check(catalog.Count>=1000,"catalog coverage");
            foreach(var (source,translation) in catalog)
            {
                Check(!string.IsNullOrWhiteSpace(translation),"empty translation");
                Check(Regex.Matches(source,@"\{[^{}]*\}").Select(m=>m.Value).Order().SequenceEqual(Regex.Matches(translation,@"\{[^{}]*\}").Select(m=>m.Value).Order()),"altered format arguments: "+source);
            }
            Check(UiLanguages.GameLanguageName("ru",code)!="Russian","localized game language");
            var external="<player {0} — Steam>";
            Check(UiLanguages.Format(code,$"Обновляю лаунчер до {external}…",$"Updating launcher to {external}…").Contains(external),"argument data preserved verbatim");
        }
        Check(UiLanguages.GameLanguageName("en","ru")=="Английский (оригинал)","English original label");
        Check(UiLanguages.Normalize("UK")=="uk"&&UiLanguages.DatePattern("uk")=="dd.MM.yyyy","Ukrainian selection and date format");
        Check(new Localization("uk")["nav.settings"]=="Налаштування","Ukrainian resources connected");
        Check(UiLanguages.GameLanguageName("en","uk")=="Англійська (оригінал)"&&UiLanguages.GameLanguageName("uk","uk")=="Українська","Ukrainian game language labels");
        return checks;
    }
}
