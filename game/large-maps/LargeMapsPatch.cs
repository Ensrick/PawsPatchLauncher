// LOCAL ONLY: use the already allocated 16-bit GTID index space. Slot zero
// remains invalid, so the final usable capacity is 65,535, never 65,536.
internal static class LargeMapsPatch
{
    internal const uint Site = 0x23f61f;
    internal static readonly byte[] Before = TerrainPatch.Hex("81be0400060000800000722d");
    internal static readonly byte[] After = TerrainPatch.Hex("81be04000600ffff0000722d");

    internal static void Validate(IMemory memory, uint image)
    {
        TerrainPatch.Expect(memory, image + Site, Before);
        // The scan wraps at 65,536; changing this bound would require a new
        // registry layout, ID representation, save format and all consumers.
        TerrainPatch.Expect(memory, image + 0x23f660, TerrainPatch.Hex("bd0000010043eb13"));
        TerrainPatch.Expect(memory, image + 0x23f685, TerrainPatch.Hex("0fb74c56048b7c2418518bc4c1e1100bca"));
    }

    internal static void Install(IMemory memory, uint image, System.Action<string> log)
    {
        Validate(memory, image);
        try
        {
            memory.WriteCode(image + Site, After);
            TerrainPatch.Expect(memory, image + Site, After);
            memory.Flush(image + Site, After.Length);
        }
        catch
        {
            memory.WriteCode(image + Site, Before);
            TerrainPatch.Expect(memory, image + Site, Before);
            memory.Flush(image + Site, Before.Length);
            throw;
        }
        log("LOCAL_LARGE_MAPS_READY objectCapacity=65535 original=32768 idBits=16 generationBits=16 tableSizeUnchanged=true");
    }
}
