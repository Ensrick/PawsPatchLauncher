using System;
using System.IO;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;

// Mandatory, UI-only part of Paw's Patch. No palette, family, sync or RNG code.
internal static class PawGamePresentation
{
    const int Size=4096;
    const int AllocationSize=8192;
    const int HudLayoutSite=0x2ac2cb;
    static readonly byte[] HudLayoutGuard={0x8b,0x86,0x88,0,0,0};
    const int GroupSpacingSite=0xdad14;
    static readonly byte[] GroupSpacingGuard={0xf3,0x0f,0x5c,0x44,0x24,0x0c};
    // Ensrick identified the stock WidescreenUniformUI path. Use that layout
    // calculation only for opted-in patch builds, without changing/saving UVars.
    // Use uniform coordinates only for KKC_GameAppState. The engine run phase
    // at 0x5f9218 is also 2 in menus and must NOT select the UI canvas.
    // Fixed-width menus, lobby and movies retain the stock 1024-wide canvas.
    const int UniformGuardSite=0x141649;
    static readonly byte[] UniformGuard={0x8b,0x80,0xe8,0,0,0,0x80,0x38,0,0x74,0x39};
    // The helper can attach after the first resolution calculation. The stock
    // layout-change detector must see our effective setting too, so its normal
    // main-thread invalidation runs once. The stored user flag stays untouched.
    const int UniformPollSite=0x13c856;
    static readonly byte[] UniformPollGuard={0x8b,0x80,0xe8,0,0,0,0x8a,0};
    const int UniformClampSite=0x141688;
    static readonly byte[] UniformClampGuard={0xf3,0x0f,0x11,0x4d,0xfc};
    internal static byte[] UniformPhaseCode(uint image)
    {
        // Bounded current app-state lookup, then match its verified native type.
        // Null/uninitialized states, loading, lobby and scoring keep stock UI.
        byte[] code={0xa1,0,0,0,0,0x83,0xf8,0x0f,0x77,0x15,
            0x8b,0x04,0x85,0,0,0,0,0x85,0xc0,0x74,0x0a,
            0x81,0x38,0,0,0,0,0x0f,0x94,0xc0,0xc3,0x30,0xc0,0xc3};
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f92f4)),0,code,1,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f92b4)),0,code,13,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x48d0dc)),0,code,23,4);
        return code;
    }
    static byte[] UniformPhaseCall(uint image,uint cave,int site,int length,bool test)
    {
        byte[] code=new byte[length];for(int i=0;i<length;i++)code[i]=0x90;
        code[0]=0xe8;
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(cave+0xd00-image-(uint)site-5))),0,code,1,4);
        if(test){code[5]=0x84;code[6]=0xc0;}
        return code;
    }
    internal static byte[] UniformButtonCode(uint image,uint cave)
    {
        // Tooltip right edge follows the logical viewport. Preserve EAX, flags
        // and the complete XMM0 value around this replacement for a plain MOV.
        byte[] code={0x9c,0x50,0x83,0xec,0x10,0xf3,0x0f,0x7f,0x04,0x24,0xa1,0,0,0,0,
            0x8b,0x80,0x88,0,0,0,0xf3,0x0f,0x10,0,0xf3,0x0f,0x5c,0x05,0,0,0,0,
            0xf3,0x0f,0x11,0x86,0x94,0,0,0,0xf3,0x0f,0x6f,0x04,0x24,0x83,0xc4,0x10,0x58,0x9d,0xc3};
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f94d0)),0,code,11,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(cave+0xdf0)),0,code,29,4);
        return code;
    }
    // BEGIN GENERATED HUD LAYOUT (build_hud_native.py)
    internal static byte[] HudLayoutCode(uint image,uint cave)
    {
        byte[] b=Convert.FromBase64String("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");
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f9228)),0,b,0x224,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f94d0)),0,b,0x249,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f9228)),0,b,0x261,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f9228)),0,b,0x282,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5f94d0)),0,b,0x523,4);
        Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x2b8185-(cave+0x1544))),0,b,0x540,4);
        return b;
    }
    // END GENERATED HUD LAYOUT
    static readonly int[] Sites={0xBF99F,0xFA979,0xC772E,0xC88BD,0x2BFA43};
    static readonly int[] Targets={0,0x400,0x600,0xa00,0xb00};
    static readonly byte[][] Expected={
        new byte[]{0xE8,0xFB,0xD3,0x1F,0},
        new byte[]{0xA1,0xC0,0x3F,0xA5,0},
        new byte[]{0xFF,0x92,0xD0,0,0,0},
        new byte[]{0xE8,0xC3,0xF8,0x1E,0},
        new byte[]{0xE8,0x80,0x74,0xFF,0xFF}
    };
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool ReadProcessMemory(IntPtr p,IntPtr a,byte[] b,int n,out IntPtr done);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool WriteProcessMemory(IntPtr p,IntPtr a,byte[] b,int n,out IntPtr done);
    [DllImport("kernel32.dll",SetLastError=true)] static extern IntPtr VirtualAllocEx(IntPtr p,IntPtr a,int size,uint type,uint protect);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualFreeEx(IntPtr p,IntPtr a,int size,uint type);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool VirtualProtectEx(IntPtr p,IntPtr a,int n,uint protect,out uint old);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool FlushInstructionCache(IntPtr p,IntPtr a,int n);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetExitCodeProcess(IntPtr p,out uint code);
    static IntPtr Add(IntPtr p,int n){return new IntPtr(p.ToInt64()+n);}
    static byte[] Resource(string name)
    {
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        using(var output=new MemoryStream())
        {
            if(stream==null) throw new InvalidDataException("Missing common UI payload: "+name);
            stream.CopyTo(output); return output.ToArray();
        }
    }
    static bool Equal(byte[] a,byte[] b)
    {
        if(a.Length!=b.Length) return false;
        for(int i=0;i<a.Length;i++) if(a[i]!=b[i]) return false;
        return true;
    }
    static byte[] ExpectedAt(int site,uint image)
    {
        byte[] bytes=(byte[])Expected[site].Clone();
        // Main-menu MOV has an absolute operand relocated by the PE loader.
        // The relative formatter CALL has identical bytes at every image base.
        if(site==1) Buffer.BlockCopy(BitConverter.GetBytes(unchecked(image+0x5F3FC0)),0,bytes,1,4);
        return bytes;
    }
    internal static string VersionSuffix(string text)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(string raw in text.Split('\n'))
        {
            string line=raw.Trim();
            if(line.Length==0 || line.StartsWith(";") || line=="[Versions]") continue;
            int split=line.IndexOf('=');
            if(split<1) throw new InvalidDataException("Invalid version metadata line");
            string key=line.Substring(0,split).Trim(),value=line.Substring(split+1).Trim();
            if((key!="ArcaneWars" && key!="PawPatch" && key!="Mod" && key!="ModVersion" && key!="PatchChannel") || values.ContainsKey(key) ||
                !Regex.IsMatch(value,@"\A[0-9A-Za-z.+_ -]{1,64}\z"))
                throw new InvalidDataException("Invalid version metadata value");
            values.Add(key,value);
        }
        if(values.ContainsKey("Mod"))
        {
            if(values.ContainsKey("ArcaneWars") || (values["Mod"]!="vanilla" && values["Mod"]!="immortals" && values["Mod"]!="arcane-wars"))
                throw new InvalidDataException("Invalid game mode metadata");
            string suffix="";
            if(values["Mod"]!="vanilla")
            {
                if(!values.ContainsKey("ModVersion")) throw new InvalidDataException("Missing mod version");
                suffix="\n"+(values["Mod"]=="immortals"?"Immortals":"Arcane Wars")+" "+values["ModVersion"];
            }
            if(values.ContainsKey("PawPatch"))
            {
                if(!values.ContainsKey("PatchChannel") || (values["PatchChannel"]!="stable" && values["PatchChannel"]!="beta"))
                    throw new InvalidDataException("Invalid patch channel");
                suffix+="\nPaw's Patch "+values["PawPatch"];
            }
            if(suffix.Length==0) throw new InvalidDataException("Empty menu label");
            return suffix;
        }
        if(!values.ContainsKey("ArcaneWars") || !values.ContainsKey("PawPatch") || values.Count!=2)
            throw new InvalidDataException("Version metadata is incomplete");
        return "\nArcane Wars "+values["ArcaneWars"]+"\nPaw's Patch "+values["PawPatch"];
    }
    static byte[] Relocate(uint image,uint cave)
    {
        byte[] bytes=Resource("PawCommonUiPayload");
        if(bytes.Length!=Size) throw new InvalidDataException("Common UI payload size changed");
        using(var r=new BinaryReader(new MemoryStream(Resource("PawCommonUiFixups"))))
        {
            uint count=r.ReadUInt32();
            if(count>128 || r.BaseStream.Length!=4+12*count) throw new InvalidDataException("Invalid UI fixups");
            var seen=new HashSet<uint>();
            for(int i=0;i<count;i++)
            {
                uint kind=r.ReadUInt32(),off=r.ReadUInt32(),value=r.ReadUInt32();
                if((off>0x7fc && (off<0xa00 || off>0xbfc)) || !seen.Add(off)) throw new InvalidDataException("Invalid UI relocation");
                uint result;
                if(kind==1) result=unchecked(image+value);
                else if(kind==2) result=unchecked(cave+value);
                else if(kind==3) result=unchecked(image+value-(cave+off+4));
                else throw new InvalidDataException("Unknown UI relocation");
                Buffer.BlockCopy(BitConverter.GetBytes(result),0,bytes,(int)off,4);
            }
        }
        return bytes;
    }
    public static int VerifyOffline()
    {
        if(!Equal(Resource("PawCommonUiPayload"),Relocate(0x460000,0x10000000)))
            throw new InvalidDataException("Common UI relocation self-test failed");
        foreach(uint image in new uint[]{0x460000,0x80000,0x6A0000})
        {
            byte[] menu=ExpectedAt(1,image);
            if(menu[0]!=0xA1 || BitConverter.ToUInt32(menu,1)!=image+0x5F3FC0 || !Equal(ExpectedAt(0,image),Expected[0]) || !Equal(ExpectedAt(2,image),Expected[2]))
                throw new InvalidDataException("Common UI ASLR signature self-test failed");
        }
        if(!Equal(ExpectedAt(1,0x460000),Expected[1])) throw new InvalidDataException("Baseline UI signature changed");
        string expected="\nArcane Wars 0.82.1.8\nPaw's Patch 1.3.72-data.8-r2+ui.1";
        if(VersionSuffix("[Versions]\nArcaneWars=0.82.1.8\nPawPatch=1.3.72-data.8-r2+ui.1")!=expected)
            throw new InvalidDataException("UI version metadata self-test failed");
        foreach(string bad in new[]{"", "ArcaneWars=<color=red>\nPawPatch=1", "ArcaneWars=1\nArcaneWars=2\nPawPatch=3"})
        {
            bool rejected=false;
            try { VersionSuffix(bad); } catch(InvalidDataException) { rejected=true; }
            if(!rejected) throw new InvalidDataException("Unsafe UI version metadata accepted");
        }
        return 0;
    }
    static void Write(IntPtr process,IntPtr address,byte[] bytes)
    {
        IntPtr done;
        if(!WriteProcessMemory(process,address,bytes,bytes.Length,out done) || done.ToInt64()!=bytes.Length)
            throw new Win32Exception(Marshal.GetLastWin32Error(),"Write common UI patch");
    }
    static void Patch(IntPtr process,IntPtr address,byte[] bytes)
    {
        uint old,ignored;
        if(!VirtualProtectEx(process,address,bytes.Length,0x40,out old)) throw new Win32Exception();
        try { Write(process,address,bytes); }
        finally { VirtualProtectEx(process,address,bytes.Length,old,out ignored); }
        if(!FlushInstructionCache(process,address,bytes.Length)) throw new Win32Exception();
    }
    public static void Install(IntPtr process,IntPtr image,string logPath)
#if LAIR_RECOVERY_TEST
    { InstallPresentation(process,image,logPath,ReleaseStartup.GameDataDirectory,false); }
#else
    { InstallPresentation(process,image,logPath,AppDomain.CurrentDomain.BaseDirectory,false); }
#endif

    public static void InstallMenuOnly(IntPtr process,IntPtr image,string logPath,string root)
    { InstallPresentation(process,image,logPath,root,true); }

    // PurePatch already owns the negative-zero call. Keep that hook single-owned
    // while enabling the shared sound widget and Pure launcher version labels.
    public static void InstallPureBeta(IntPtr process,IntPtr image,string logPath,string root)
    { InstallPresentation(process,image,logPath,root,true,true); }

    internal static void ValidateData(string root)
    {
        VersionSuffix(File.ReadAllText(Path.Combine(root,"paws_patch_versions.ini"),Encoding.UTF8));
        if(!File.Exists(Path.Combine(root,"data","UI","Menus","main.tgi")))
            throw new FileNotFoundException("Missing common UI main menu layout");
    }

    static void InstallPresentation(IntPtr process,IntPtr image,string logPath,string root,bool menuOnly,bool pureBeta=false)
    {
        VerifyOffline();
        string suffix=VersionSuffix(File.ReadAllText(Path.Combine(root,menuOnly?"paws_launch_versions.ini":"paws_patch_versions.ini"),Encoding.UTF8));
        if(!File.Exists(Path.Combine(root,"data","UI","Menus","main.tgi")))
            throw new FileNotFoundException("Missing common UI main menu layout");
        // Older data packages and menu-only launches have no sound-button asset.
        bool soundButton=(!menuOnly || pureBeta) && File.Exists(Path.Combine(root,"data","UI","Game","PawGoldSound.png")) &&
            File.Exists(Path.Combine(root,"data","UI","Game","game_interface.tgi")) &&
            File.ReadAllText(Path.Combine(root,"data","UI","Game","game_interface.tgi")).Contains("[PawGoldSoundButton Template=PushButtonWidget]");
        bool uniformButton=soundButton && File.ReadAllText(Path.Combine(root,"data","UI","Game","game_interface.tgi")).Contains("PawUniformButtonRevision2");
        // Steam decrypts code during boot. Validate all selected sites before writes.
        var timer=System.Diagnostics.Stopwatch.StartNew();
        int firstSite=menuOnly?1:0;
        int lastSite=soundButton?Sites.Length:2;
#if PAW_UNIFORM_UI
        bool uniform=!menuOnly || pureBeta;
#else
        bool uniform=false;
#endif
        if(uniform)
        {
            foreach(var guard in new[]{Tuple.Create(UniformGuardSite,UniformGuard),Tuple.Create(UniformPollSite,UniformPollGuard),Tuple.Create(UniformClampSite,UniformClampGuard),Tuple.Create(HudLayoutSite,HudLayoutGuard),Tuple.Create(GroupSpacingSite,GroupSpacingGuard)})
            while(true)
            {
                byte[] found=new byte[guard.Item2.Length]; IntPtr done; uint exitCode;
                if(ReadProcessMemory(process,Add(image,guard.Item1),found,found.Length,out done) && done.ToInt64()==found.Length && Equal(found,guard.Item2)) break;
                if(timer.ElapsedMilliseconds>60000 || !GetExitCodeProcess(process,out exitCode) || exitCode!=259)
                    throw new InvalidOperationException("Uniform UI signature mismatch; no UI patch applied");
                Thread.Sleep(20);
            }
        }
        for(int i=firstSite;i<lastSite;i++)
        {
            while(true)
            {
                byte[] found=new byte[Expected[i].Length]; IntPtr done; uint exitCode;
                if(ReadProcessMemory(process,Add(image,Sites[i]),found,found.Length,out done) && done.ToInt64()==found.Length && Equal(found,ExpectedAt(i,(uint)image.ToInt64()))) break;
                if(timer.ElapsedMilliseconds>60000 || !GetExitCodeProcess(process,out exitCode) || exitCode!=259)
                    throw new InvalidOperationException("Common UI hook signature mismatch; no UI patch applied");
                Thread.Sleep(20);
            }
        }
        IntPtr cave=VirtualAllocEx(process,IntPtr.Zero,AllocationSize,0x3000,0x40);
        if(cave==IntPtr.Zero) throw new Win32Exception();
        int attempted=firstSite;
        bool uniformAttempted=false;
        bool uniformPollAttempted=false;
        bool uniformClampAttempted=false;
        bool hudLayoutAttempted=false;
        bool groupSpacingAttempted=false;
        try
        {
            byte[] payload=Relocate((uint)image.ToInt64(),(uint)cave.ToInt64());
            Array.Resize(ref payload,AllocationSize);
            byte[] label=Encoding.Unicode.GetBytes(suffix+"\0");
            if(label.Length>0x200) throw new InvalidDataException("Version labels too long");
            Buffer.BlockCopy(label,0,payload,0x800,label.Length);
            if(uniform)
            {
                byte[] hud=HudLayoutCode((uint)image.ToInt64(),(uint)cave.ToInt64());
                Buffer.BlockCopy(hud,0,payload,0x1000,hud.Length);
                // Ordinary tooltips share the viewport anchor; the Paw button
                // retains its separate title-only placement above its icon.
                if(payload[0xaeb]!=0xe9 || unchecked((uint)(cave.ToInt64()+0xaf0+BitConverter.ToInt32(payload,0xaec)))!=unchecked((uint)image.ToInt64()+0x2b8185))
                    throw new InvalidDataException("Ordinary tooltip branch changed");
                Buffer.BlockCopy(BitConverter.GetBytes(0x1500-0xaf0),0,payload,0xaec,4);
                // Preserve the stock 1024-wide layout on 5:4 and narrower
                // screens. Wider layouts use the stock aspect calculation.
                // XMM1 is already the new width; preserve all other registers.
                for(int i=0xc00;i<0xc12;i++) if(payload[i]!=0) throw new InvalidDataException("Uniform UI cave overlaps common UI payload");
                byte[] clamp={0xf3,0x0f,0x5f,0x0d,0,0,0,0,0xf3,0x0f,0x11,0x4d,0xfc,0xe9,0,0,0,0};
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked((uint)image.ToInt64()+0x4b2e38)),0,clamp,4,4);
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(image.ToInt64()+UniformClampSite+5-cave.ToInt64()-0xc12))),0,clamp,14,4);
                Buffer.BlockCopy(clamp,0,payload,0xc00,clamp.Length);
                byte[] phase=UniformPhaseCode((uint)image.ToInt64());
                for(int i=0xd00;i<0xd00+phase.Length;i++)if(payload[i]!=0)throw new InvalidDataException("Uniform phase cave overlaps UI payload");
                Buffer.BlockCopy(phase,0,payload,0xd00,phase.Length);
                if(uniformButton)
                {
                    byte[] guard={0xc7,0x86,0x94,0,0,0,0,0,0x7f,0x44};
                    for(int i=0;i<guard.Length;i++)if(payload[0xa57+i]!=guard[i])throw new InvalidDataException("Uniform tooltip signature changed");
                    for(int i=0xd80;i<0xdf4;i++)if(payload[i]!=0)throw new InvalidDataException("Uniform button cave overlaps UI payload");
                    byte[] button=UniformButtonCode((uint)image.ToInt64(),(uint)cave.ToInt64());
                    Buffer.BlockCopy(button,0,payload,0xd80,button.Length);
                    Buffer.BlockCopy(BitConverter.GetBytes(4f),0,payload,0xdf0,4);
                    payload[0xa57]=0xe8;
                    Buffer.BlockCopy(BitConverter.GetBytes(0xd80-0xa5c),0,payload,0xa58,4);
                    for(int i=0xa5c;i<0xa61;i++)payload[i]=0x90;
                    if(BitConverter.ToSingle(payload,0xa67)!=512f)throw new InvalidDataException("Uniform tooltip height changed");
                    Buffer.BlockCopy(BitConverter.GetBytes(510f),0,payload,0xa67,4);
                }
            }
            Write(process,cave,payload);
            if(!FlushInstructionCache(process,cave,AllocationSize)) throw new Win32Exception();
            if(uniform)
            {
                uniformAttempted=true;
                Patch(process,Add(image,UniformGuardSite),UniformPhaseCall((uint)image.ToInt64(),(uint)cave.ToInt64(),UniformGuardSite,9,true));
                byte[] clampJump={0xe9,0,0,0,0};
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(cave.ToInt64()+0xc00-image.ToInt64()-UniformClampSite-5))),0,clampJump,1,4);
                uniformClampAttempted=true;
                Patch(process,Add(image,UniformClampSite),clampJump);
                uniformPollAttempted=true;
                Patch(process,Add(image,UniformPollSite),UniformPhaseCall((uint)image.ToInt64(),(uint)cave.ToInt64(),UniformPollSite,8,false));
                byte[] hudCall={0xe8,0,0,0,0,0x90};
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(cave.ToInt64()+0x1000-image.ToInt64()-HudLayoutSite-5))),0,hudCall,1,4);
                hudLayoutAttempted=true;
                Patch(process,Add(image,HudLayoutSite),hudCall);
                byte[] groupCall={0xe8,0,0,0,0,0x90};
                Buffer.BlockCopy(BitConverter.GetBytes(unchecked((int)(cave.ToInt64()+0x1600-image.ToInt64()-GroupSpacingSite-5))),0,groupCall,1,4);
                groupSpacingAttempted=true;
                Patch(process,Add(image,GroupSpacingSite),groupCall);
            }
            for(int i=firstSite;i<lastSite;i++)
            {
                byte[] branch=new byte[Expected[i].Length]; branch[0]=(byte)(i==1?0xe9:0xe8);
                for(int j=5;j<branch.Length;j++) branch[j]=0x90;
                int relative=unchecked((int)(cave.ToInt64()+Targets[i]-image.ToInt64()-Sites[i]-5));
                Buffer.BlockCopy(BitConverter.GetBytes(relative),0,branch,1,4);
                attempted=i+1; Patch(process,Add(image,Sites[i]),branch);
            }
            File.AppendAllText(logPath,DateTime.Now.ToString("O")+(menuOnly?" MENU_ONLY r1; versions=":" COMMON_UI r1; versions=")+suffix.Replace('\n',';')+
                "; negativeZero="+(menuOnly?"untouched":"display-only")+"; goldSoundButton="+soundButton+"; goldSoundTitleOnlyHover="+soundButton+"; uniformUi="+uniform+"; simulationUntouched=true; cave=0x"+cave.ToInt64().ToString("X8")+Environment.NewLine);
        }
        catch
        {
            bool restored=true;
            if(groupSpacingAttempted) try { Patch(process,Add(image,GroupSpacingSite),GroupSpacingGuard); } catch { restored=false; }
            if(hudLayoutAttempted) try { Patch(process,Add(image,HudLayoutSite),HudLayoutGuard); } catch { restored=false; }
            if(uniformPollAttempted) try { Patch(process,Add(image,UniformPollSite),UniformPollGuard); } catch { restored=false; }
            if(uniformClampAttempted) try { Patch(process,Add(image,UniformClampSite),UniformClampGuard); } catch { restored=false; }
            if(uniformAttempted) try { Patch(process,Add(image,UniformGuardSite),UniformGuard); } catch { restored=false; }
            for(int i=attempted-1;i>=firstSite;i--) try { Patch(process,Add(image,Sites[i]),ExpectedAt(i,(uint)image.ToInt64())); } catch { restored=false; }
            if(restored) VirtualFreeEx(process,cave,0,0x8000);
            throw;
        }
    }
}
