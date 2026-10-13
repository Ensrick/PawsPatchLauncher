using System;
using System.Diagnostics;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;

internal static class EngineCrashFixesPatch
{
    static int pid; static uint data; static DateTime next;
    static byte[] last; static Action<string> logger;
    // The stock IgnoreAsserts script command (handler RVA 13FC6D) stores this byte at 13FC82. The assertion handler
    // logs "ASSERT: %s" first and, at 301A3E, returns Ignore instead of opening its system-modal Abort/Retry/Ignore box
    // when the byte is set. Data write only: no game code changes.
    internal const uint AssertFlag=0x60294C,AssertStore=0x13FC82,AssertCheck=0x301A3E;
    internal static byte[] AssertStoreCode(uint image){return TerrainPatch.Hex("0F9505").Concat(BitConverter.GetBytes(image+AssertFlag)).ToArray();}
    internal static byte[] AssertCheckCode(uint image){return TerrainPatch.Hex("803D").Concat(BitConverter.GetBytes(image+AssertFlag)).Concat(new byte[]{0}).ToArray();}
    internal static void Validate(IMemory m,uint image)
    {
        for(int i=0;i<EngineCrashPayload.Sites.Length;i++)
            TerrainPatch.Expect(m,image+EngineCrashPayload.Sites[i],EngineCrashPayload.Original(i,image));
        TerrainPatch.Expect(m,image+0x31EAAC,TerrainPatch.Hex("85C974368B018B4004FFD0"));
        TerrainPatch.Expect(m,image+0x31EB1A,TerrainPatch.Hex("8B7508468975083B7738"));
        TerrainPatch.Expect(m,image+0x14C03F,TerrainPatch.Hex("8B068D4A018B7604"));
        TerrainPatch.Expect(m,image+0x14C04E,TerrainPatch.Hex("0F44CA8BD185F675E8"));
        TerrainPatch.Expect(m,image+AssertStore,AssertStoreCode(image));
        TerrainPatch.Expect(m,image+AssertCheck,AssertCheckCode(image));
    }
    internal static uint ResolveHandlerRegistration(Process game)
    {
        uint address=unchecked((uint)GetProcAddress(GetModuleHandle("kernel32.dll"),"AddVectoredExceptionHandler").ToInt32());
        if(address==0)throw new IOException("Windows exception registration is unavailable.");
        using(var local=Process.GetCurrentProcess())
        {
            var owner=local.Modules.Cast<ProcessModule>().Single(m=>address>=U(m.BaseAddress)&&address<U(m.BaseAddress)+(uint)m.ModuleMemorySize);
            game.Refresh();var remote=game.Modules.Cast<ProcessModule>().Single(m=>String.Equals(m.ModuleName,owner.ModuleName,StringComparison.OrdinalIgnoreCase));
            if(ReleaseStartup.Hash(owner.FileName)!=ReleaseStartup.Hash(remote.FileName))throw new IOException("Exception registration library identity differs.");
            return U(remote.BaseAddress)+address-U(owner.BaseAddress);
        }
    }
    internal static uint Install(IMemory m,uint image,uint addHandler,Action<string> log)
    {
        if(addHandler==0)throw new ArgumentException("Missing Windows registration entry");
        Validate(m,image);uint cave=0;int attempted=-1;bool safe=true;byte[] assertFlag=null;
        try
        {
            assertFlag=m.Read(image+AssertFlag,1);
            cave=m.Allocate(EngineCrashPayload.Allocation);
            byte[] code=EngineCrashPayload.Build(image,cave);
            m.Write(cave,code);TerrainPatch.Expect(m,cave,code);
            var zero=new byte[EngineCrashPayload.Allocation-EngineCrashPayload.DataOffset];
            Buffer.BlockCopy(BitConverter.GetBytes(addHandler),0,zero,44,4);
            m.Write(cave+EngineCrashPayload.DataOffset,zero);TerrainPatch.Expect(m,cave+EngineCrashPayload.DataOffset,zero);
            m.MakeExecutable(cave,EngineCrashPayload.DataOffset);m.Flush(cave,code.Length);
            for(int i=0;i<EngineCrashPayload.Sites.Length;i++)
            {
                uint site=image+EngineCrashPayload.Sites[i];
                byte[] hook=Enumerable.Repeat((byte)0x90,EngineCrashPayload.Originals[i].Length/2).ToArray();
                byte[] jump=TerrainPatch.Call(site,cave+EngineCrashPayload.Offsets[i]);jump[0]=0xE9;
                Buffer.BlockCopy(jump,0,hook,0,jump.Length);
                attempted=i;safe=false;m.WriteCode(site,hook);TerrainPatch.Expect(m,site,hook);m.Flush(site,hook.Length);
            }
            attempted=EngineCrashPayload.Sites.Length;
            m.Write(image+AssertFlag,new byte[]{1});TerrainPatch.Expect(m,image+AssertFlag,new byte[]{1});
            log("ENGINE_CRASH_FIXES r3; shared animation target guard; missing network client guard; observer team-command guards; assertion dialogs ignored (logged); cave=0x"+cave.ToString("X8"));
            return cave;
        }
        catch
        {
            safe=true;
            if(attempted==EngineCrashPayload.Sites.Length)
            {
                try{m.Write(image+AssertFlag,assertFlag);TerrainPatch.Expect(m,image+AssertFlag,assertFlag);}
                catch(Exception e){safe=false;log("ENGINE_CRASH_ROLLBACK_UNCERTAIN "+e.Message);}
                attempted--;
            }
            for(int i=attempted;i>=0;i--)try
            {
                byte[] b=EngineCrashPayload.Original(i,image);
                m.WriteCode(image+EngineCrashPayload.Sites[i],b);TerrainPatch.Expect(m,image+EngineCrashPayload.Sites[i],b);m.Flush(image+EngineCrashPayload.Sites[i],b.Length);
            }
            catch(Exception e){safe=false;log("ENGINE_CRASH_ROLLBACK_UNCERTAIN "+e.Message);}
            if(cave!=0&&safe)m.Free(cave);throw;
        }
    }
    internal static void Monitor(int processId,uint cave,Action<string> log)
    {pid=processId;data=cave+EngineCrashPayload.DataOffset;logger=log;last=new byte[56];next=DateTime.MinValue;}
    internal static void Tick()
    {
        if(data==0||DateTime.UtcNow<next)return;next=DateTime.UtcNow.AddSeconds(1);
        try
        {
            byte[] b;using(var m=new NativeMemory(pid))b=m.Read(data,56);
            if(last.SequenceEqual(b))return;last=b;
            logger("ENGINE_CRASH_COUNTERS animationSkipped="+U(b,0)+" deadTarget="+U(b,4)+" invalidVtable="+U(b,8)+" invalidTypeFunction="+U(b,12)+" unreadableTarget="+U(b,16)+" missingNetworkClient="+U(b,20)+" lastController=0x"+U(b,24).ToString("X8")+" lastTarget=0x"+U(b,28).ToString("X8")+" reason="+U(b,32)+" probeRegistered="+(U(b,40)!=0)+" registrationFailed="+U(b,48)+" observerTeamCommandsIgnored="+U(b,52));
        }
        catch { next=DateTime.UtcNow.AddSeconds(10); }
    }
    static uint U(byte[] b,int i){return BitConverter.ToUInt32(b,i);}
    static uint U(IntPtr p){return unchecked((uint)p.ToInt32());}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
}
