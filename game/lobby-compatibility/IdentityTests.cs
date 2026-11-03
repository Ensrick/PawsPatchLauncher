using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
class IdentityTests {
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();static int count;
    static void Check(bool b){count++;if(!b)throw new Exception("Identity regression "+count);}
    static Dictionary<string,object> Module(string version){
        var file=new Dictionary<string,object>{{"path","data/Game/example.tgi"},{"sha256",new string('A',64)}};
        return new Dictionary<string,object>{{"version",version},{"enabled",true},{"files",new object[]{file}}};
    }
    static string Token(Dictionary<string,object> s,string hash,string helper){bool ru;return PawLobbyCompatibility.Identity(Json.Serialize(s),new string('A',64),helper,hash,out ru);}
    static void Main(){
        var settings=new Dictionary<string,object>{{"mod","arcane-wars"},{"pawPatchEnabled",true},{"russianLocalization",false},{"customPlayerColors",true},{"desyncMode","continue"},{"independentHostility",true},{"roamingSpawnMode","x4"},{"additionalRoamingCompanies",true},{"siegeBalance",true},{"disablePowersAndShards",true},{"largeMapSizes",true}};
        var modules=new Dictionary<string,object>{{"arcane-wars",Module("0.82.1.8-clean.1")},{"pawpatch-core",Module(PawLobbyCompatibility.Version)},{"common-ui",Module("1.3.72-ui.6-beta.7")}};
        var s=new Dictionary<string,object>{{"appliedSettings",settings},{"modules",modules}};
        var h=new string('B',64);const string helper="k2_paws_lobby_colors_mp_sync_1372.exe";var original=Token(s,h,helper);
        Check(original.StartsWith("PWLC1|1.3.72|arcane-wars|0.82.1.8|" + PawLobbyCompatibility.Version + "|"));
        for(int i=0;i<64;i++){var changed=h.ToCharArray();changed[i]='C';Check(Token(s,new string(changed),helper)!=original);}
        foreach(var name in new[]{"localization-ru","game-voice-ru","game-localization-en","aw-localization-ru","game-text-ru","game-localization-cs","game-localization-uk","localization-cs","localization-uk","immortals-localization-cs","immortals-localization-uk"}){modules[name]=Module("999.9");Check(Token(s,h,helper)==original);modules.Remove(name);}
        foreach(var name in new[]{"language","gameTextLanguage","gameVoiceLanguage","notificationSoundName","gamePath"}){settings[name]="different-local-value";Check(Token(s,h,helper)==original);settings.Remove(name);}
        settings["russianLocalization"]=true;Check(Token(s,h,helper)==original);bool ru;PawLobbyCompatibility.Identity(Json.Serialize(s),new string('A',64),helper,h,out ru);Check(ru);
        settings["russianLocalization"]=false;
#if COMPANY_BEHAVIOR
        settings["ignoreZeroMorale"]=false;Check(Token(s,h,helper)==original);
        settings["ignoreZeroMorale"]=true;Check(Token(s,h,helper)!=original);
        settings.Remove("ignoreZeroMorale");Check(Token(s,h,helper)==original);
#endif
        foreach(var key in new[]{"customPlayerColors","independentHostility","additionalRoamingCompanies","siegeBalance","disablePowersAndShards","largeMapSizes"}){settings[key]=false;Check(Token(s,h,helper)!=original);settings[key]=true;}
        settings["roamingSpawnMode"]="x2";Check(Token(s,h,helper)!=original);settings["roamingSpawnMode"]="x4";
        settings["desyncMode"]="stop";Check(Token(s,h,helper)!=original);settings["desyncMode"]="continue";
        foreach(var culture in new[]{"ru-RU","en-US","tr-TR","de-DE"}){System.Threading.Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo(culture);Check(Token(s,h,helper)==original);}
        s["modules"]=modules.Reverse().ToDictionary(p=>p.Key,p=>p.Value);Check(Token(s,h,helper)==original);s["modules"]=modules;
        Check(Token(s,h,"k2_paws_ui_1372.exe")!=original);
        modules["pawpatch-core"]=Module("0.3.0-beta.6");bool failed=false;try{Token(s,h,helper);}catch(InvalidDataException){failed=true;}Check(failed);
        Console.WriteLine("PASS "+count+" identity checks: EXE-only differences, components, languages, cultures, ordering and stale packages");
    }
}
