using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

// The optional file package and native behavior are one installed setting.
// Do not infer it from a stale user's preference or the helper filename.
internal static class PawAiOptions
{
    internal static bool Enabled;
    internal static bool Read(string root)
    {
        string path = Path.Combine(root, @".pawpatch\state.json");
        if (!File.Exists(path)) throw new InvalidDataException("Apply the patch settings in the launcher first.");
        string text = File.ReadAllText(path);
        if (text.Length > 16 * 1024 * 1024) throw new InvalidDataException("Installation state is too large.");
        var json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 64 };
        return Parse((Dictionary<string, object>)json.DeserializeObject(text));
    }
    internal static bool Parse(Dictionary<string, object> state)
    {
        var settings = (Dictionary<string, object>)state["appliedSettings"];
        var modules = (Dictionary<string, object>)state["modules"];
        object value;
        bool enabled = settings.TryGetValue("improvedAi", out value) && Object.Equals(value, true);
        string module = "ai-improvements";
        bool supported = settings.TryGetValue("mod", out value) && Object.Equals(value, "arcane-wars");
        bool channelSupported = settings.TryGetValue("channel", out value)
            && (Object.Equals(value, "stable") || Object.Equals(value, "beta"));
#if PAW_PURE_EXTENDED
        module = "pure-ai-improvements";
        supported = settings.TryGetValue("mod", out value)
            && (Object.Equals(value, "vanilla") || Object.Equals(value, "immortals"));
#endif
        bool installed = modules.TryGetValue(module, out value)
            && ((Dictionary<string, object>)value).TryGetValue("enabled", out value) && Object.Equals(value, true);
        if (enabled != installed || enabled && (
            !supported || !channelSupported
            || !Object.Equals(settings["pawPatchEnabled"], true)
            || settings.TryGetValue("dataOnly", out value) && Object.Equals(value, true)))
            throw new InvalidDataException("AI settings and installed files differ. Apply settings in the launcher.");
        return enabled;
    }
}
