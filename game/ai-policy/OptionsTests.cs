using System;
using System.Collections.Generic;
internal static class OptionsTests
{
    static int Main()
    {
        int count = 0;
        foreach (bool enabled in new[] { false, true })
        foreach (bool installed in new[] { false, true })
        foreach (string channel in new[] { "stable", "beta", "invalid" })
        foreach (string mod in new[] { "arcane-wars", "vanilla", "immortals" })
        foreach (bool core in new[] { false, true })
        foreach (bool dataOnly in new[] { false, true })
        {
            var settings = new Dictionary<string, object> { {"improvedAi",enabled}, {"mod",mod}, {"channel",channel}, {"pawPatchEnabled",core}, {"dataOnly",dataOnly} };
            string module = "ai-improvements";
            bool supported = mod == "arcane-wars" && (channel == "stable" || channel == "beta");
#if PAW_PURE_EXTENDED
            module = "pure-ai-improvements";
            supported = (mod == "vanilla" || mod == "immortals") && (channel == "stable" || channel == "beta");
#endif
            var modules = new Dictionary<string, object> { {module, new Dictionary<string, object> { {"enabled", installed} }} };
            var state = new Dictionary<string, object> { {"appliedSettings",settings}, {"modules",modules} };
            bool valid = enabled == installed && (!enabled || supported && core && !dataOnly);
            bool failed = false; bool result = false;
            try { result = PawAiOptions.Parse(state); } catch (System.IO.InvalidDataException) { failed = true; }
            if (failed == valid || valid && result != enabled) throw new Exception("AI option mismatch");
            count++;
        }
        var legacy = new Dictionary<string, object> { {"appliedSettings",new Dictionary<string, object>()}, {"modules",new Dictionary<string, object>()} };
        if (PawAiOptions.Parse(legacy)) throw new Exception("Old launcher must default to disabled");
        Console.WriteLine("AI_OPTION_TESTS_PASS " + (count + 1)); return 0;
    }
}
