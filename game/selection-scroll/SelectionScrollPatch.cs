using System;
internal static class SelectionScrollPatch {
 internal static void Validate(IMemory m,uint image){
  TerrainPatch.Expect(m,image+0xed127,TerrainPatch.Hex("83f8067d3b"));
  for(int i=0;i<SelectionScrollPayload.Sites.Length;i++)TerrainPatch.Expect(m,image+SelectionScrollPayload.Sites[i],BitConverter.GetBytes(image+SelectionScrollPayload.Targets[i]));
 }
 internal static uint Install(IMemory m,uint image,Action<string> log){
  Validate(m,image);uint cave=0;int attempted=-1;bool free=true,cap=false;
  try {
   cave=m.Allocate(SelectionScrollPayload.Allocation);byte[] code=SelectionScrollPayload.Build(image,cave);
   m.Write(cave,code);TerrainPatch.Expect(m,cave,code);
   m.Write(cave+(uint)SelectionScrollPayload.DataOffset,new byte[SelectionScrollPayload.Allocation-SelectionScrollPayload.DataOffset]);
   m.MakeExecutable(cave,SelectionScrollPayload.DataOffset);m.Flush(cave,code.Length);
   for(int i=0;i<SelectionScrollPayload.Sites.Length;i++){
    attempted=i;free=false;byte[] pointer=BitConverter.GetBytes(cave+SelectionScrollPayload.Offsets[i]);
    m.WriteCode(image+SelectionScrollPayload.Sites[i],pointer);TerrainPatch.Expect(m,image+SelectionScrollPayload.Sites[i],pointer);
   }
   // Remove admission cap only after every presentation/input hook is installed.
   cap=true;m.WriteCode(image+0xed12a,TerrainPatch.Hex("9090"));TerrainPatch.Expect(m,image+0xed12a,TerrainPatch.Hex("9090"));
   log("SELECTION_SCROLL r2; dynamic selection; adaptive cards; hidden cards retain destination flags; wheel paging; full formation recipients; cave=0x"+cave.ToString("X8"));return cave;
  }catch{
   free=true;
   if(cap)try{m.WriteCode(image+0xed12a,TerrainPatch.Hex("7d3b"));TerrainPatch.Expect(m,image+0xed12a,TerrainPatch.Hex("7d3b"));}catch(Exception e){free=false;log("SELECTION_SCROLL_CAP_ROLLBACK_UNCERTAIN "+e.Message);}
   for(int i=attempted;i>=0;i--)try{byte[] b=BitConverter.GetBytes(image+SelectionScrollPayload.Targets[i]);m.WriteCode(image+SelectionScrollPayload.Sites[i],b);TerrainPatch.Expect(m,image+SelectionScrollPayload.Sites[i],b);}catch(Exception e){free=false;log("SELECTION_SCROLL_ROLLBACK_UNCERTAIN "+e.Message);}
   if(cave!=0&&free)m.Free(cave);throw;
  }
 }
}
