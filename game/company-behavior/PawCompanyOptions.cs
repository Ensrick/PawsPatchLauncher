using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

internal static class PawCompanyOptions
{
    internal static bool IgnoreZeroMorale;
    internal static bool Read(string root)
    {
        string text=File.ReadAllText(Path.Combine(root,@".pawpatch\state.json"));
        if(text.Length>16*1024*1024)throw new InvalidDataException("Installation state is too large.");
        var json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024,RecursionLimit=64};
        var state=(Dictionary<string,object>)json.DeserializeObject(text);
        return Parse((Dictionary<string,object>)state["appliedSettings"]);
    }
    internal static bool Parse(Dictionary<string,object> settings)
    {
        object value;
        if(!settings.TryGetValue("ignoreZeroMorale",out value))return false;
        if(!(value is bool))throw new InvalidDataException("Invalid exhaustion setting.");
        bool enabled=(bool)value;
        if(!enabled)return false;
#if PAW_PURE_EXTENDED
        throw new InvalidDataException("Ignore zero morale is available only in Arcane Wars.");
#else
        if(!settings.TryGetValue("mod",out value)||!Object.Equals(value,"arcane-wars")
            ||!settings.TryGetValue("pawPatchEnabled",out value)||!Object.Equals(value,true)
            ||settings.TryGetValue("dataOnly",out value)&&Object.Equals(value,true)
            ||!settings.TryGetValue("channel",out value)||!(Object.Equals(value,"beta")||Object.Equals(value,"stable")))
            throw new InvalidDataException("Apply the Arcane Wars exhaustion setting in the launcher.");
        return true;
#endif
    }
}
