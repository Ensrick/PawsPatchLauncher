using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

// Included in the eight Arcane Wars helpers. No network or UI polling.
internal static class PawLobbyCompatibility
{
#if LOCAL_LARGE_MAPS || LARGE_MAPS
    internal const string Version = LargeMapsLocal.PatchVersion;
#elif PAW_PURE_EXTENDED
    internal const string Version = PawPureFixes.PureChannel.PatchVersion;
#else
    internal const string Version = "0.4.0-beta.1";
#endif
    private static byte[] configuration;
    private static string nativePath;
    private static readonly uint[] Sites = {0x151092,0x150e40,0x151c8a,0x1519ef,0x151bf3,0x7d3eb,0x77417};
    private static readonly uint[] Originals = {0x7c92f,0x7c92f,0x149033,0x149046,0x23fb6,0x23ee7,0x77fdc};
    private static string Hex(byte[] b) {return BitConverter.ToString(b).Replace("-", "");}
    private static string Hash(byte[] b) {using(var sha=SHA256.Create())return Hex(sha.ComputeHash(b));}
    private static string FileHash(string p) {using(var f=File.OpenRead(p))using(var sha=SHA256.Create())return Hex(sha.ComputeHash(f));}
    private static Dictionary<string,object> Obj(object x) {var d=x as Dictionary<string,object>;if(d==null)throw new InvalidDataException("Invalid installed configuration.");return d;}
    private static object Value(Dictionary<string,object> d,string k) {object v;return d.TryGetValue(k,out v)?v:null;}
    private static string Text(Dictionary<string,object> d,string k) {return Convert.ToString(Value(d,k),CultureInfo.InvariantCulture);}
    private static bool Flag(Dictionary<string,object> d,string k) {return Object.Equals(Value(d,k),true);}
    private static string Normalize(string p) {
        p=p.Replace('\\','/').ToLowerInvariant();
        if(p.Length==0||p.StartsWith("/")||p.Contains(":")||p.Split('/').Any(s=>s.Length==0||s=="."||s==".."))throw new InvalidDataException("Unsafe manifest path.");
        return p;
    }
    private static string Field(string s) {if(!Regex.IsMatch(s??"","\\A[A-Za-z0-9.+_-]{1,48}\\z"))throw new InvalidDataException("Invalid installed version.");return s;}
    private static bool LanguagePackage(string id) {return id.IndexOf("localization",StringComparison.OrdinalIgnoreCase)>=0||id.StartsWith("game-voice",StringComparison.Ordinal)||id.StartsWith("game-text",StringComparison.Ordinal)||id=="immortals-text-fixes";}

    // Package manifests cover the applied component selection; actual native
    // executable hashes distinguish equal labels with different EXE contents.
    // The game's original depot/data checks remain enabled.
    internal static string Identity(string json,string exeHash,string helperName,string helperHash,out bool russian)
    {
        if(json.Length>16*1024*1024)throw new InvalidDataException("Installation state is too large.");
        var serializer=new JavaScriptSerializer {MaxJsonLength=16*1024*1024,RecursionLimit=64};
        var state=Obj(serializer.DeserializeObject(json));var settings=Obj(Value(state,"appliedSettings"));var modules=Obj(Value(state,"modules"));
#if PAW_PURE_EXTENDED
        string mode = Text(settings,"mod");
        if((mode!="vanilla" && mode!="immortals") || Text(settings,"channel")!="beta" || !Flag(settings,"pawPatchEnabled") || Flag(settings,"dataOnly"))
            throw new InvalidDataException("Select the Vanilla or Immortals beta with Paw's Patch enabled.");
        var core=Obj(Value(modules,"pure-fixes-data"));
        if(!Flag(core,"enabled") || Text(core,"version")!=Version)
            throw new InvalidDataException("Update and apply Paw's Patch "+Version+" in the launcher.");
        string modVersion="1.3.72";
        if(mode=="immortals") {
            var mod=Obj(Value(modules,"immortals"));
            if(!Flag(mod,"enabled"))throw new InvalidDataException("Missing installed Immortals module.");
            modVersion=Field(Text(mod,"version"));
        }
#else
        string mode="arcane-wars";
        if(Text(settings,"mod")!="arcane-wars"||!Flag(settings,"pawPatchEnabled")||Flag(settings,"dataOnly"))throw new InvalidDataException("Выберите Arcane Wars и включите Paw's Patch в лаунчере. / Select Arcane Wars with Paw's Patch enabled.");
        var core=Obj(Value(modules,"pawpatch-core"));var mod=Obj(Value(modules,"arcane-wars"));
        if(!Flag(core,"enabled")||Text(core,"version")!=Version||!Flag(mod,"enabled"))throw new InvalidDataException("Нужны файлы Paw's Patch "+Version+". Обновите и примените настройки в лаунчере. / Update and apply Paw's Patch "+Version+" in the launcher.");
        var modVersion=Field(Regex.Replace(Text(mod,"version"),@"-clean\.\d+$",""));
#endif
        if(!Regex.IsMatch(exeHash,"\\A[0-9A-F]{64}\\z")||!Regex.IsMatch(helperHash,"\\A[0-9A-F]{64}\\z"))throw new InvalidDataException("Invalid executable digest.");
        var lines=new List<string>{"protocol=1","exe="+exeHash,"helper="+Normalize(helperName)+":"+helperHash};
        foreach(var entry in modules.OrderBy(p=>p.Key,StringComparer.Ordinal)) {
            var m=Obj(entry.Value);if(!Flag(m,"enabled")||LanguagePackage(entry.Key))continue;
            lines.Add("module="+Field(entry.Key)+":"+Field(Text(m,"version")));
            var files=Value(m,"files") as object[];if(files==null)throw new InvalidDataException("Missing installed file manifest.");
            foreach(var file in files.Select(Obj).OrderBy(f=>Normalize(Text(f,"path")),StringComparer.Ordinal)) {
                var digest=Text(file,"sha256").ToUpperInvariant();if(!Regex.IsMatch(digest,"\\A[0-9A-F]{64}\\z"))throw new InvalidDataException("Invalid installed file digest.");
                lines.Add("file="+Normalize(Text(file,"path"))+":"+digest);
            }
            var remove=Value(m,"remove") as object[];
            if(remove!=null)foreach(var p in remove.Select(Convert.ToString).Select(Normalize).OrderBy(x=>x,StringComparer.Ordinal))lines.Add("remove="+p);
        }
        foreach(var key in new[]{"customPlayerColors","desyncMode","independentHostility","roamingSpawnMode","additionalRoamingCompanies","siegeBalance","disablePowersAndShards","largeMapSizes"})
            lines.Add("setting="+key+":"+Text(settings,key).ToLowerInvariant());
        // Game message language follows game text, never affects the identity.
        russian=Flag(settings,"russianLocalization");
#if LOCAL_LARGE_MAPS || LARGE_MAPS
        LargeMapsPatch.ConfigureWarning(Text(settings,"roamingSpawnMode"),PawGameText.Language==""?(russian?1u:0u):PawGameText.LanguageId);
#endif
        return "PWLC1|1.3.72|"+mode+"|"+modVersion+"|"+Version+"|"+Hash(Encoding.UTF8.GetBytes(String.Join("\n",lines)));
    }
    private static string InstalledIdentity(string root,out bool russian) {
        string helper=Assembly.GetExecutingAssembly().Location;
        string token=Identity(File.ReadAllText(Path.Combine(root,@".pawpatch\state.json")),FileHash(Path.Combine(root,"k2.exe")),Path.GetFileName(helper),FileHash(helper),out russian);
        if(token.Length>=256)throw new InvalidDataException("Compatibility identity is too long.");
        return token;
    }
    // Same installed-state/version guard as launch, without extracting a DLL,
    // creating a process or touching runtime state.
    internal static void ValidateInstallation(string root) {
        bool russian;InstalledIdentity(root,out russian);
    }
    internal static void Prepare(string root) {
        bool russian;string token=InstalledIdentity(root,out russian);
        configuration=new byte[264];BitConverter.GetBytes(0x31434c50u).CopyTo(configuration,0);BitConverter.GetBytes(PawGameText.Language==""?(russian?1u:0u):PawGameText.LanguageId).CopyTo(configuration,4);Encoding.ASCII.GetBytes(token).CopyTo(configuration,8);
        byte[] payload;
        using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PawLobbyCompatibilityNative")) {
            if(s==null||s.Length<1024||s.Length>1024*1024)throw new InvalidDataException("Missing lobby compatibility resource.");
            using(var output=new MemoryStream()){s.CopyTo(output);payload=output.ToArray();}
        }
        string digest=Hash(payload);
        // Keep the native module next to this game's managed installation.
        // Some legacy Steam compatibility environments cannot load from AppData.
        string folder=Path.Combine(root,".pawpatch","native",digest);
        Directory.CreateDirectory(folder);nativePath=Path.Combine(folder,"paws_lobby_compatibility.dll");
        if(!File.Exists(nativePath)) {
            string temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try {File.WriteAllBytes(temporary,payload);try{File.Move(temporary,nativePath);}catch(IOException){if(!File.Exists(nativePath))throw;}}
            finally {if(File.Exists(temporary))File.Delete(temporary);}
        }
        if(FileHash(nativePath)!=digest)throw new InvalidDataException("Lobby compatibility resource integrity check failed.");
    }
    internal static void Install(Process game,IntPtr image,Action<string> log) {
        if(configuration==null||nativePath==null)throw new InvalidOperationException("Compatibility check was not prepared.");
        using(var native=new Installer(game,image))native.Install(nativePath,configuration);
        log("LOBBY_COMPATIBILITY_READY protocol=1 patch="+Version+" nativeCalls=7 browserLabel=Paws-Launcher identity="+Encoding.ASCII.GetString(configuration,8,256).TrimEnd('\0'));
    }

    private sealed class Installer:IDisposable {
        private IntPtr handle;private readonly Process game;private readonly uint image;private readonly DateTime started;
        internal Installer(Process p,IntPtr b){game=p;image=U(b);started=p.StartTime.ToUniversalTime();handle=OpenProcess(0x00100c3a,false,p.Id);if(handle==IntPtr.Zero)throw new IOException("Cannot open fresh game for compatibility check.");}
        private static uint U(IntPtr p){return unchecked((uint)p.ToInt32());}
        private static IntPtr P(uint p){return new IntPtr(unchecked((int)p));}
        private byte[] Read(uint address,int n){var b=new byte[n];IntPtr done;if(!ReadProcessMemory(handle,P(address),b,n,out done)||done.ToInt32()!=n)throw new IOException("Lobby check read failed.");return b;}
        private IntPtr Allocate(byte[] data){var p=VirtualAllocEx(handle,IntPtr.Zero,data.Length,0x3000,4);IntPtr n;if(p==IntPtr.Zero||!WriteProcessMemory(handle,p,data,data.Length,out n)||n.ToInt32()!=data.Length)throw new IOException("Lobby check allocation failed.");return p;}
        private uint Invoke(uint address,IntPtr data,uint timeout){uint tid;var t=CreateRemoteThread(handle,IntPtr.Zero,0,P(address),data,0,out tid);if(t==IntPtr.Zero)throw new IOException("Lobby check thread failed.");try{if(WaitForSingleObject(t,timeout)!=0)throw new IOException("Lobby check initialization timed out.");uint exit;if(!GetExitCodeThread(t,out exit))throw new IOException("Lobby check thread status unavailable.");return exit;}finally{CloseHandle(t);}}
        private ProcessModule Module(string name){game.Refresh();return game.Modules.Cast<ProcessModule>().Single(m=>m.ModuleName.Equals(name,StringComparison.OrdinalIgnoreCase));}
        private uint LoadRemoteLibrary(uint load,string dll){
            var path=Allocate(Encoding.Unicode.GetBytes(dll+"\0"));var result=Allocate(new byte[8]);byte[] code;
            using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)){
                writer.Write((byte)0x68);writer.Write(U(path));writer.Write((byte)0xb8);writer.Write(load);
                writer.Write(new byte[]{0xff,0xd0,0xa3});writer.Write(U(result));
                // Capture this thread's Win32 error directly from the x86 TEB.
                writer.Write(new byte[]{0x64,0x8b,0x0d,0x18,0,0,0,0x8b,0x49,0x34,0x89,0x0d});writer.Write(U(result)+4);
                writer.Write(new byte[]{0x33,0xc0,0xc2,4,0});code=output.ToArray();
            }
            var stub=Allocate(code);uint old;
            if(!VirtualProtectEx(handle,stub,code.Length,0x20,out old)||!FlushInstructionCache(handle,stub,code.Length))throw new IOException("Cannot prepare system loader call.");
            Invoke(U(stub),IntPtr.Zero,10000);var value=Read(U(result),8);
            VirtualFreeEx(handle,path,0,0x8000);VirtualFreeEx(handle,result,0,0x8000);VirtualFreeEx(handle,stub,0,0x8000);
            uint module=BitConverter.ToUInt32(value,0);if(module==0)throw new IOException("Game could not load lobby compatibility library. Win32="+BitConverter.ToUInt32(value,4));return module;
        }
        private void Guard(){
            if(game.HasExited||game.StartTime.ToUniversalTime()!=started||U(game.MainModule.BaseAddress)!=image)throw new IOException("Fresh game process identity changed.");
            for(int i=0;i<Sites.Length;i++){var b=Read(image+Sites[i],5);if(b[0]!=0xe8||unchecked(image+Sites[i]+5+BitConverter.ToUInt32(b,1))!=image+Originals[i])throw new IOException("Lobby compatibility guard failed at "+Sites[i].ToString("X"));}
        }
        internal void Install(string dll,byte[] config){
            Guard();IntPtr local=LoadLibraryEx(dll,IntPtr.Zero,1);if(local==IntPtr.Zero)throw new IOException("Cannot read lobby compatibility library.");
            try{
                IntPtr export=GetProcAddress(local,"PawInstallQueued");if(export==IntPtr.Zero)export=GetProcAddress(local,"_PawInstallQueued@4");if(export==IntPtr.Zero)throw new IOException("Missing lobby install export.");
                uint load=U(GetProcAddress(GetModuleHandle("kernel32.dll"),"LoadLibraryW"));
                ProcessModule owner=Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Single(m=>load>=U(m.BaseAddress)&&load<U(m.BaseAddress)+(uint)m.ModuleMemorySize);
                var target=Module(owner.ModuleName);if(FileHash(owner.FileName)!=FileHash(target.FileName))throw new IOException("System loader identity differs.");
                uint remoteLoad=U(target.BaseAddress)+load-U(owner.BaseAddress);
                uint module=LoadRemoteLibrary(remoteLoad,dll);
                byte[] request=new byte[config.Length+12];config.CopyTo(request,12);
                IntPtr arg=Allocate(request);bool suspended=false;uint tid;
                var worker=CreateRemoteThread(handle,IntPtr.Zero,0,P(module+U(export)-U(local)),arg,0,out tid);
                if(worker==IntPtr.Zero)throw new IOException("Cannot start lobby installation worker.");
                try{
                    var timer=Stopwatch.StartNew();
                    while(BitConverter.ToInt32(Read(U(arg),4),0)!=1){
                        if(game.HasExited||WaitForSingleObject(worker,0)==0||timer.ElapsedMilliseconds>10000)throw new IOException("Lobby installation worker did not initialize.");
                        System.Threading.Thread.Sleep(10);
                    }
                    if(NtSuspendProcess(handle)!=0)throw new IOException("Cannot pause fresh game for lobby installation.");suspended=true;Guard();
                    // This worker already completed DLL thread notifications.
                    if(ResumeThread(worker)!=1)throw new IOException("Unexpected lobby worker suspend count.");
                    IntPtr written;var go=BitConverter.GetBytes(1);
                    if(!WriteProcessMemory(handle,P(U(arg)+4),go,4,out written)||written.ToInt32()!=4)throw new IOException("Cannot release lobby installation worker.");
                    timer.Restart();
                    // Observe the explicit result before DLL_THREAD_DETACH, which
                    // may need a loader lock held by another paused game thread.
                    while(BitConverter.ToInt32(Read(U(arg),4),0)!=2){
                        if(WaitForSingleObject(worker,0)==0||timer.ElapsedMilliseconds>5000)throw new IOException("Lobby hook installation did not complete.");
                        System.Threading.Thread.Sleep(1);
                    }
                    uint result=BitConverter.ToUInt32(Read(U(arg)+8,4),0);
                    if(result!=0)throw new IOException("Lobby compatibility installation failed: "+result);
                    for(int i=0;i<Sites.Length;i++){var b=Read(image+Sites[i],5);if(b[0]!=0xe8||unchecked(image+Sites[i]+5+BitConverter.ToUInt32(b,1))==image+Originals[i])throw new IOException("Lobby hook readback failed.");}
                    VirtualFreeEx(handle,arg,0,0x8000);
                }finally{CloseHandle(worker);if(suspended&&NtResumeProcess(handle)!=0)throw new IOException("Cannot resume fresh game after lobby installation.");}
            }finally{FreeLibrary(local);}
        }
        public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
    }
    [DllImport("kernel32",SetLastError=true)]private static extern IntPtr OpenProcess(uint access,bool inherit,int pid);
    [DllImport("kernel32")]private static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32")]private static extern bool ReadProcessMemory(IntPtr h,IntPtr a,byte[] b,int n,out IntPtr read);
    [DllImport("kernel32")]private static extern bool WriteProcessMemory(IntPtr h,IntPtr a,byte[] b,int n,out IntPtr written);
    [DllImport("kernel32")]private static extern IntPtr VirtualAllocEx(IntPtr h,IntPtr a,int n,uint type,uint protect);
    [DllImport("kernel32")]private static extern bool VirtualFreeEx(IntPtr h,IntPtr a,int n,uint type);
    [DllImport("kernel32")]private static extern bool VirtualProtectEx(IntPtr h,IntPtr a,int n,uint protect,out uint old);
    [DllImport("kernel32")]private static extern bool FlushInstructionCache(IntPtr h,IntPtr a,int n);
    [DllImport("kernel32")]private static extern IntPtr CreateRemoteThread(IntPtr h,IntPtr attr,uint stack,IntPtr fn,IntPtr arg,uint flags,out uint id);
    [DllImport("kernel32")]private static extern uint WaitForSingleObject(IntPtr h,uint ms);
    [DllImport("kernel32")]private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32")]private static extern bool GetExitCodeThread(IntPtr h,out uint code);
    [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
    [DllImport("kernel32",CharSet=CharSet.Ansi)]private static extern IntPtr GetProcAddress(IntPtr module,string name);
    [DllImport("kernel32")]private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32",CharSet=CharSet.Unicode)]private static extern IntPtr GetModuleHandle(string name);
    [DllImport("ntdll")]private static extern int NtSuspendProcess(IntPtr h);
    [DllImport("ntdll")]private static extern int NtResumeProcess(IntPtr h);
}
