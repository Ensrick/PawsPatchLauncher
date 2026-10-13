// QA host for two owned local processes. Invokes the unchanged packaged helper's
// composition methods; production's one-game launch guard remains unchanged.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
class InstalledHarness {
 static Assembly helper;
 const BindingFlags Flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance;
 static object Call(string type,string method,params object[] args){return helper.GetType(type,true).GetMethod(method,Flags).Invoke(null,args);}
 static void Instance(object obj,string method){obj.GetType().GetMethod(method,Flags).Invoke(obj,null);}
 static int Main(string[] args){
  Process game=null;
  try {
   if(args.Length!=1)throw new ArgumentException("Private game folder required");
   string root=Path.GetFullPath(args[0]),path=Path.Combine(root,"k2.exe");
   if(!File.Exists(Path.Combine(root,"reconnect-test.marker")))throw new IOException("Private fixture marker required");
   helper=Assembly.LoadFile(Path.Combine(root,"k2_paws_pure_fixes_1372.exe"));
   Call("PawPureFixes.Program","Preflight",root);Call("PawPureFixes.PureBetaRuntime","Prepare",root);
   var start=new ProcessStartInfo(path,"+Exec Mods/rig.txt"){WorkingDirectory=root,UseShellExecute=false};
   start.EnvironmentVariables["PAWS_RIG_EXTERNAL_SCRIPT"]="1";
   game=Process.Start(start); Console.WriteLine("OWNED_GAME "+game.Id);Console.Out.Flush();
   DateTime began=game.StartTime.ToUniversalTime(),limit=DateTime.UtcNow.AddSeconds(45);
   while(true){game.Refresh();if(game.HasExited)throw new IOException("Owned game exited during startup");if(game.MainWindowHandle!=IntPtr.Zero)break;if(DateTime.UtcNow>limit)throw new TimeoutException("Game window");Thread.Sleep(50);}
   if(!game.MainModule.FileName.Equals(path,StringComparison.OrdinalIgnoreCase))throw new IOException("Game identity changed");
   IntPtr image=game.MainModule.BaseAddress;uint address=unchecked((uint)image.ToInt32());
   Action<string> log=message=>{File.AppendAllText(Path.Combine(root,"harness-status.log"),DateTime.UtcNow.ToString("O")+" "+message+"\n");};
   using(var memory=(IDisposable)Activator.CreateInstance(helper.GetType("PawPureFixes.NativeMemory",true),Flags,null,new object[]{game.Id,path,began},null)){
    Call("PawPureFixes.Program","WaitForCode",memory,address,game);
    uint registration=(uint)Call("EngineCrashFixesPatch","ResolveHandlerRegistration",game);
    Instance(memory,"Suspend");
    try {
     Call("PawPureFixes.PureBetaRuntime","Validate",memory,address);
     Call("PawPureFixes.PureChannel","Install",memory,address);
     Call("PawPureFixes.PureBetaRuntime","Install",memory,address,registration,game,root,log);
    }finally{Instance(memory,"Resume");}
   }
   Call("PawGamePresentation","InstallPureBeta",game.Handle,image,Path.Combine(root,"harness-status.log"),root);
   Call("PawPureFixes.PureBetaRuntime","Finish",game,image,root,log);
   Console.WriteLine("PACKAGED_HELPER_READY");Console.Out.Flush();
   while(!game.WaitForExit(1000))Call("PawPureFixes.PureBetaRuntime","Tick");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  finally{if(game!=null){if(!game.HasExited){game.Kill();game.WaitForExit(10000);}game.Dispose();}}
 }
}
