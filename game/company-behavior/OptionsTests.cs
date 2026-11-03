using System;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
internal static class CompanyOptionsTests
{
    public static void Main()
    {
        int checks=0;
        string root=Path.Combine(Path.GetTempPath(),"paw-company-options-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,".pawpatch"));
        try
        {
            foreach(bool enabled in new[]{false,true})
            foreach(string mod in new[]{"arcane-wars","vanilla","immortals"})
            foreach(bool core in new[]{false,true})
            foreach(bool data in new[]{false,true})
            foreach(string channel in new[]{"stable","beta","bad"})
            {
                var settings=new Dictionary<string,object>{{"ignoreZeroMorale",enabled},{"mod",mod},{"pawPatchEnabled",core},{"dataOnly",data},{"channel",channel}};
                bool valid=!enabled || mod=="arcane-wars"&&core&&!data&&channel!="bad";
#if PAW_PURE_EXTENDED
                valid=!enabled;
#endif
                File.WriteAllText(Path.Combine(root,@".pawpatch\state.json"),new JavaScriptSerializer().Serialize(new {appliedSettings=settings}));
                bool failed=false,result=false;
                try { result=PawCompanyOptions.Read(root); } catch(InvalidDataException) { failed=true; }
                if(failed==valid || valid&&result!=enabled)throw new Exception("Options check "+checks);
                checks++;
            }
            Console.WriteLine("COMPANY_OPTIONS_PASS "+checks);
        }
        finally { Directory.Delete(root,true); }
    }
}
