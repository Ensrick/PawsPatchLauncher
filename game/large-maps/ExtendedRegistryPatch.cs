// LOCAL ONLY. Installed before registry construction, while the freshly
// launched game is suspended. Existing running matches are never modified.
internal static class LargeMapsPatch
{
    private static uint warningConfiguration;
    internal static void ConfigureWarning(string frequency,uint language)
    {
        uint multiplier=frequency=="x2"?2u:frequency=="x4"?4u:1u;
        warningConfiguration=multiplier|((language<6?language:0)<<8);
    }
    internal static void Validate(IMemory m,uint image)
    {
        byte[][] originals=ExtendedRegistryPayload.Originals(image);
        for(int i=0;i<originals.Length;i++)TerrainPatch.Expect(m,image+ExtendedRegistryPayload.Sites[i],originals[i]);
        // The global registry must not exist yet: changing an active world's
        // interpretation would invalidate every saved reference in memory.
        if(System.BitConverter.ToUInt32(m.Read(image+0x5ef72c,4),0)!=0)
            throw new System.InvalidOperationException("Large-map registry must be installed before world creation.");
    }
    internal static void Install(IMemory m,uint image,System.Action<string> log)
    {
        Validate(m,image);uint cave=0;int attempted=-1;bool safe=true;
        byte[][] old=ExtendedRegistryPayload.Originals(image);
        try {
            cave=m.Allocate(ExtendedRegistryPayload.Allocation);
            byte[] code=ExtendedRegistryPayload.Build(image,cave);
            m.Write(cave,code);TerrainPatch.Expect(m,cave,code);
            byte[] state=new byte[ExtendedRegistryPayload.Allocation-ExtendedRegistryPayload.DataOffset];
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(image),0,state,0,4);
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(cave+ExtendedRegistryPayload.Call2),0,state,4,4);
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(18),0,state,8,4);
            System.Buffer.BlockCopy(System.BitConverter.GetBytes(warningConfiguration),0,state,28,4);
            m.Write(cave+(uint)ExtendedRegistryPayload.DataOffset,state);
            TerrainPatch.Expect(m,cave+(uint)ExtendedRegistryPayload.DataOffset,state);
            log("LOCAL_LARGE_MAP_WARNING frequency="+(warningConfiguration&255)+" language="+((warningConfiguration>>8)&255)+" threshold=1536x1536");
            m.MakeExecutable(cave,ExtendedRegistryPayload.DataOffset);m.Flush(cave,code.Length);
            for(int i=0;i<old.Length;i++){
                byte[] patch=new byte[old[i].Length];for(int j=0;j<patch.Length;j++)patch[j]=0x90;
                patch[0]=0xe9;System.Buffer.BlockCopy(System.BitConverter.GetBytes(unchecked(cave+ExtendedRegistryPayload.Offsets[i]-image-ExtendedRegistryPayload.Sites[i]-5)),0,patch,1,4);
                attempted=i;safe=false;m.WriteCode(image+ExtendedRegistryPayload.Sites[i],patch);
                TerrainPatch.Expect(m,image+ExtendedRegistryPayload.Sites[i],patch);m.Flush(image+ExtendedRegistryPayload.Sites[i],patch.Length);
            }
            log("LOCAL_LARGE_MAPS_EXTENDED_READY capacity=262143 primary=65535 mapObjects=196608 oldSaveMode=16 newSaveMode=18 cave=0x"+cave.ToString("X8")+" state=0x"+(cave+(uint)ExtendedRegistryPayload.DataOffset).ToString("X8"));
        } catch {
            safe=true;
            for(int i=attempted;i>=0;i--)try {m.WriteCode(image+ExtendedRegistryPayload.Sites[i],old[i]);TerrainPatch.Expect(m,image+ExtendedRegistryPayload.Sites[i],old[i]);m.Flush(image+ExtendedRegistryPayload.Sites[i],old[i].Length);}catch(System.Exception e){safe=false;log("LARGE_MAPS_ROLLBACK_UNCERTAIN "+e.Message);}
            if(cave!=0&&safe)m.Free(cave);throw;
        }
    }
}
