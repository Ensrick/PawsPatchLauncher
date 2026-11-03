using System;
internal static class GroupFormationPatch
{
    internal const uint HookRva=0x2337ba;
    internal static readonly byte[] Original=TerrainPatch.Hex("3B5C24147D16");
    internal static readonly byte[] Guard=TerrainPatch.Hex("8BC885C90F84A20000003B5C24147D168B87B80100008B4018F30F1024D8F30F1054D804EB52");
    internal static void Validate(IMemory m,uint image){TerrainPatch.Expect(m,image+HookRva-10,Guard);}
    internal static byte[] Jump(uint image,uint cave){byte[] b={0xe9,0,0,0,0,0x90};Array.Copy(BitConverter.GetBytes(unchecked(cave-(image+HookRva+5))),0,b,1,4);return b;}
    internal static uint Install(IMemory m,uint image,Action<string> log)
    {
        Validate(m,image);uint cave=0;bool attempted=false,safe=true;
        try{
            cave=m.Allocate(4096);byte[] b=GroupFormationPayload.Build(image,cave);
            m.Write(cave,b);TerrainPatch.Expect(m,cave,b);m.MakeExecutable(cave,4096);m.Flush(cave,b.Length);
            b=Jump(image,cave);attempted=true;safe=false;
            m.WriteCode(image+HookRva,b);TerrainPatch.Expect(m,image+HookRva,b);m.Flush(image+HookRva,b.Length);
            log("GROUP_FORMATION r1; native rows extended beyond six; native centering; custom layouts preserved; cave=0x"+cave.ToString("X8"));return cave;
        }catch{
            if(attempted){try{m.WriteCode(image+HookRva,Original);TerrainPatch.Expect(m,image+HookRva,Original);m.Flush(image+HookRva,Original.Length);safe=true;}catch(Exception e){log("GROUP_FORMATION_ROLLBACK_UNCERTAIN: "+e.Message);}}
            if(cave!=0&&safe){try{m.Free(cave);}catch(Exception e){log("GROUP_FORMATION_FREE_FAILED: "+e.Message);}}throw;
        }
    }
}
