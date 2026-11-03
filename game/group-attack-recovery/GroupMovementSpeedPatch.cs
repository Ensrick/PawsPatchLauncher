using System;

// Keep native formation waiting, but never require a company to reach a speed
// above its own maximum. Waiting only applies while the group has an active
// coordinated movement command (mode 1), not idle membership or combat support.
// Apply the same rules in CAI update and both UAI gates.
internal static class GroupMovementSpeedPatch
{
    internal static readonly uint[] Sites = { 0x26f69e, 0x27dc2c, 0x27f499, 0x26f68a, 0x27dc18, 0x27f485 };
    internal static readonly string[] Originals = { "F30F10000F2F472C", "F30F10000F2F4624", "F30F10000F2F4624", "E8E102E2FF", "E8531DE1FF", "E8E604E1FF" };
    internal static readonly string[] Guards = { "F30F10000F2F472C760E8B4F04C7473000000000", "F30F10000F2F46247712F30F104E24", "F30F10000F2F4624760E8BCEE843E4FFFF", "E8E102E2FF84C07423", "E8531DE1FF84C07415", "E8E604E1FF84C07423" };
    internal static void Validate(IMemory memory, uint image)
    {
        for (int i=0;i<Sites.Length;i++) TerrainPatch.Expect(memory,image+Sites[i],TerrainPatch.Hex(Guards[i]));
    }
    internal static byte[] Jump(int index,uint image,uint cave)
    {
        byte[] b=index<3 ? new byte[]{0xe9,0,0,0,0,0x90,0x90,0x90} : new byte[]{0xe8,0,0,0,0};
        Array.Copy(BitConverter.GetBytes(unchecked(cave+(uint)index*256-(image+Sites[index]+5))),0,b,1,4);return b;
    }
    internal static uint Install(IMemory memory,uint image,Action<string> log)
    {
        Validate(memory,image); uint cave=0;int attempted=0;bool safe=true;
        try
        {
            cave=memory.Allocate(4096);
            for(int i=0;i<Sites.Length;i++) {uint at=cave+(uint)i*256;byte[] code=GroupMovementSpeedPayload.Build(i,image,at);memory.Write(at,code);TerrainPatch.Expect(memory,at,code);}
            memory.MakeExecutable(cave,4096);memory.Flush(cave,Sites.Length*256);
            for(int i=0;i<Sites.Length;i++) {attempted=i+1;safe=false;byte[] b=Jump(i,image,cave);memory.WriteCode(image+Sites[i],b);TerrainPatch.Expect(memory,image+Sites[i],b);memory.Flush(image+Sites[i],b.Length);}
            log("GROUP_MOVEMENT_SPEED r2; attainable speed and active formation waiting only; cave=0x"+cave.ToString("X8"));return cave;
        }
        catch
        {
            safe=true;
            for(int i=attempted-1;i>=0;i--)
            {
                try {byte[] b=TerrainPatch.Hex(Originals[i]);memory.WriteCode(image+Sites[i],b);TerrainPatch.Expect(memory,image+Sites[i],b);memory.Flush(image+Sites[i],b.Length);}
                catch(Exception e) {safe=false;log("GROUP_MOVEMENT_SPEED_ROLLBACK_UNCERTAIN: "+e.Message);}
            }
            if(cave!=0&&safe) {try {memory.Free(cave);}catch(Exception e){log("GROUP_MOVEMENT_SPEED_FREE_FAILED: "+e.Message);}}
            throw;
        }
    }
}
