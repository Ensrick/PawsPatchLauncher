using System;
using System.IO;
using System.Linq;

// 1.3.72 TGC_AdminNode leaves its local task status (+0xa0) uninitialized.
// Host takeover sends it even in a lobby which has never loaded a world.
// Initialize the producer; do not change enum decoding or accept invalid input.
internal static class TaskStatusPatch
{
    internal const uint Site = 0x149760;
    internal static readonly byte[] Original = Hex("8b4df48bc7");
    internal static byte[] Hex(string value) { return Enumerable.Range(0,value.Length/2).Select(i=>Convert.ToByte(value.Substring(i*2,2),16)).ToArray(); }
    static void Expect(IMemory memory,uint address,byte[] expected)
    { if(!memory.Read(address,expected.Length).SequenceEqual(expected))throw new InvalidOperationException("Task status code differs at 0x"+address.ToString("X8")); }
    static byte[] Jump(uint from,uint to)
    { var bytes=new byte[5];bytes[0]=0xe9;Buffer.BlockCopy(BitConverter.GetBytes(unchecked(to-from-5)),0,bytes,1,4);return bytes; }
    internal static byte[] Build(uint image,uint cave)
    {
        // At the constructor's successful epilogue EDI is the new AdminNode.
        // MOV preserves flags, all registers and the existing SEH epilogue.
        var bytes=Hex("c787a0000000000000008b4df48bc7e900000000");
        Buffer.BlockCopy(Jump(cave+15,image+Site+5),0,bytes,15,5);return bytes;
    }
    internal static void Validate(IMemory m,uint image)
    {
        Expect(m,image+Site,Original);
        Expect(m,image+0x149565,Hex("8db79800000033db"));
        Expect(m,image+0x14957c,Hex("8d8fa4000000c645fc01"));
        Expect(m,image+0x14af73,Hex("8b81a00000008d4df86a00"));
        Expect(m,image+0x148d89,Hex("6a05ff74240cff74240ce84b20f2ff83c40cc3"));
        Expect(m,image+0x148d9c,Hex("558bec516a058d45fc50ff7508e8771ff2ff"));
        Expect(m,image+0x149765,Hex("899f840200005f5e5b64890d00000000c9c3"));
    }
    internal static uint Install(IMemory m,uint image,Action<string> log)
    {
        Validate(m,image);
        uint admin=BitConverter.ToUInt32(m.Read(image+0x5f3fec,4),0);
        byte[] previous=null;
        if(admin!=0)
        {
            // Helpers install only on a fresh launch, with all game threads paused.
            // Refuse an active session rather than overwrite a legitimate task.
            uint session=BitConverter.ToUInt32(m.Read(image+0x5f3fe4,4),0);
            Expect(m,admin,BitConverter.GetBytes(image+0x4b69e4));
            if(session==0 || BitConverter.ToUInt32(m.Read(session+0xf0,4),0)!=0 ||
               BitConverter.ToUInt32(m.Read(admin+0x2e4,4),0)!=0 ||
               BitConverter.ToUInt32(m.Read(admin+0x2cc,4),0)!=0)
                throw new InvalidOperationException("Task status initialization requires a fresh offline session.");
            previous=m.Read(admin+0xa0,4);
        }
        uint cave=0;bool attempted=false,changed=false,safe=true;
        try
        {
            cave=m.Allocate(4096);var code=Build(image,cave);
            m.Write(cave,code);Expect(m,cave,code);m.MakeExecutable(cave,4096);m.Flush(cave,code.Length);
            if(previous!=null){changed=true;m.Write(admin+0xa0,new byte[4]);Expect(m,admin+0xa0,new byte[4]);}
            attempted=true;var jump=Jump(image+Site,cave);
            m.WriteCode(image+Site,jump);Expect(m,image+Site,jump);m.Flush(image+Site,jump.Length);
            log("TASK_STATUS_INIT r1; future constructors fixed; existing="+(previous!=null)+
                (previous==null?"":" previous=0x"+BitConverter.ToUInt32(previous,0).ToString("X8")));
            return cave;
        }
        catch
        {
            if(attempted)try{m.WriteCode(image+Site,Original);Expect(m,image+Site,Original);m.Flush(image+Site,Original.Length);}catch{safe=false;}
            if(changed)try{m.Write(admin+0xa0,previous);Expect(m,admin+0xa0,previous);}catch{safe=false;}
            if(cave!=0&&safe)m.Free(cave);
            if(!safe)log("TASK_STATUS_ROLLBACK_UNCERTAIN; allocated code retained");
            throw;
        }
    }
}
