using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

internal static class GraphicsDiagnostics
{
    // The worker is embedded in each helper and launched automatically. An
    // out-of-process writer can preserve the original faulting thread context.
    internal static void Start(Process game,string root,Action<string> log)
    {
        try
        {
            if(game.HasExited) return;
            string expected=Path.GetFullPath(Path.Combine(root,"k2.exe"));
            if(!String.Equals(game.MainModule.FileName,expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected diagnostic target");
            byte[] bytes;
            using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("PawsGraphicsRecorder"))
            using(var output=new MemoryStream()){if(input==null)throw new InvalidDataException("Missing recorder");input.CopyTo(output);bytes=output.ToArray();}
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");
            string directory=Path.Combine(root,".pawpatch","graphics-diagnostics",hash);
            Directory.CreateDirectory(directory);string exe=Path.Combine(directory,"PawsGraphicsRecorder.exe");
            if(!File.Exists(exe))
            {
                string temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
                File.WriteAllBytes(temp,bytes);
                try{File.Move(temp,exe);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
            using(var file=File.OpenRead(exe))using(var sha=SHA256.Create())
                if(BitConverter.ToString(sha.ComputeHash(file)).Replace("-","")!=hash)throw new InvalidDataException("Recorder hash mismatch");
            string reports=Path.Combine(root,"Logs","PawsGraphics");Directory.CreateDirectory(reports);
            string stem=Path.Combine(reports,"log-graphics-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+game.Id);
            string args=game.Id+" "+game.StartTime.ToUniversalTime().ToFileTimeUtc().ToString("X")+" \""+expected+"\" \""+stem+"\"";
            var start=new ProcessStartInfo(exe,args){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=root};
            using(var worker=Process.Start(start))log("GRAPHICS_DIAGNOSTICS r2; graphics and invalid-call first chance; worker="+worker.Id+" report="+stem+"; local only");
        }
        catch(Exception e){log("GRAPHICS_DIAGNOSTICS_UNAVAILABLE "+e.GetType().Name+": "+e.Message);}
    }
}
