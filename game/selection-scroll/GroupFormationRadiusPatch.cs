using System;
internal static class GroupFormationRadiusPatch
{
 internal static readonly uint[] Sites={0x29941f};
 internal const uint Target=0x299424;
 internal static byte[] Original(uint image){byte[] b={0xb8,0,0,0,0};Array.Copy(BitConverter.GetBytes(image+0x442ce7),0,b,1,4);return b;}
 internal static byte[] Jump(uint at,uint target){byte[] b={0xe9,0,0,0,0};Array.Copy(BitConverter.GetBytes(unchecked(target-at-5)),0,b,1,4);return b;}
 internal static void Validate(IMemory m,uint image){foreach(uint site in Sites)TerrainPatch.Expect(m,image+site,Original(image));}
 internal static uint Install(IMemory m,uint image,Action<string> log)
 {
  Validate(m,image);uint cave=0;int attempted=0;bool safe=true;
  try{
   cave=m.Allocate(GroupFormationRadiusPayload.Allocation);byte[] b=GroupFormationRadiusPayload.Build(image,cave);
   m.Write(cave,b);TerrainPatch.Expect(m,cave,b);m.MakeExecutable(cave,b.Length);m.Flush(cave,b.Length);
   foreach(uint site in Sites){attempted++;safe=false;b=Jump(image+site,cave+GroupFormationRadiusPayload.Offset);m.WriteCode(image+site,b);TerrainPatch.Expect(m,image+site,b);m.Flush(image+site,b.Length);}
   log("GROUP_FORMATION_RADIUS r1; destination mask follows extended native rows; individual pathfinding preserved; cave=0x"+cave.ToString("X8"));return cave;
  }catch{
   if(attempted>0){safe=true;for(int i=attempted-1;i>=0;i--){uint at=image+Sites[i];try{byte[] b=Original(image);m.WriteCode(at,b);TerrainPatch.Expect(m,at,b);m.Flush(at,b.Length);}catch(Exception e){safe=false;log("GROUP_FORMATION_RADIUS_ROLLBACK_UNCERTAIN: "+e.Message);}}}
   if(cave!=0&&safe){try{m.Free(cave);}catch(Exception e){log("GROUP_FORMATION_RADIUS_FREE_FAILED: "+e.Message);}}throw;
  }
 }
}
