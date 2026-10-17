using System;

internal static class AiPolicyRuntime
{
    internal static void Validate(IMemory m,uint b) {
        for(int i=0;i<AiPolicyPayload.Sites.Length;i++)TerrainPatch.Expect(m,b+AiPolicyPayload.GuardStarts[i],AiPolicyPayload.Guard(i,b));
    }
    internal static void Install(IMemory m,uint b,int processId,string gameDir,Action<string> log) {
        uint mem=InstallNative(m,b,log);
        log("AI_POLICY r39; gameplay hooks installed; action logging and periodic snapshots disabled; cave=0x"+mem.ToString("X8"));
    }
    internal static uint InstallNative(IMemory m,uint b,Action<string> log) {
        Validate(m,b);uint mem=0;int attempted=-1;bool free=true;
        try {
            mem=m.Allocate(AiPolicyPayload.Allocation);var code=AiPolicyPayload.Build(b,mem);
            m.Write(mem,code);TerrainPatch.Expect(m,mem,code);
            var initial=new byte[AiPolicyPayload.Allocation-AiPolicyPayload.DataOffset];
            Buffer.BlockCopy(BitConverter.GetBytes(15u),0,initial,4,4);
            Buffer.BlockCopy(BitConverter.GetBytes(mem+(uint)AiPolicyPayload.QueryOffset),0,initial,AiPolicyPayload.QueryPointerOffset,4);
            m.Write(mem+(uint)AiPolicyPayload.DataOffset,initial);
            m.MakeExecutable(mem,AiPolicyPayload.CodeSize);m.Flush(mem,code.Length);
            for(int i=0;i<AiPolicyPayload.Sites.Length;i++) {
                attempted=i;free=false;var call=TerrainPatch.Call(b+AiPolicyPayload.Sites[i],mem+AiPolicyPayload.Offsets[i]);
                m.WriteCode(b+AiPolicyPayload.Sites[i],call);TerrainPatch.Expect(m,b+AiPolicyPayload.Sites[i],call);m.Flush(b+AiPolicyPayload.Sites[i],5);
            }
            return mem;
        } catch {
            free=true;
            for(int i=attempted;i>=0;i--)try {var original=TerrainPatch.Hex(AiPolicyPayload.Originals[i]);m.WriteCode(b+AiPolicyPayload.Sites[i],original);TerrainPatch.Expect(m,b+AiPolicyPayload.Sites[i],original);m.Flush(b+AiPolicyPayload.Sites[i],5);}catch(Exception e){free=false;log("AI_POLICY_ROLLBACK_UNCERTAIN: "+e.Message);}
            if(mem!=0&&free)m.Free(mem);throw;
        }
    }
    // Retained for existing helper call sites; no process reads, snapshots or disk writes.
    internal static void Tick() { }
}
