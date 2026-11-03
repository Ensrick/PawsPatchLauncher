using System;

// Disable only CAI's Exhausted transition. The shared morale decision, Rout,
// Stun and command dispatch remain native. Existing exhausted saves leave
// through the native state cleanup on their next update.
internal static class CompanyExhaustionPatch
{
    internal const uint EntryRva = 0x2778C6, UpdateRva = 0x274A1A;
    internal static readonly byte[] EntryDisabled = TerrainPatch.Hex("B000C39090");
    internal static readonly byte[] UpdateDisabled = TerrainPatch.Hex("B802000000");
    internal static byte[] EntryOriginal(uint image)
    {
        byte[] bytes = new byte[] {0xB8,0,0,0,0};
        Array.Copy(BitConverter.GetBytes(image+0x441276),0,bytes,1,4);
        return bytes;
    }
    internal static void Validate(IMemory memory, uint image)
    {
        TerrainPatch.Expect(memory,image+EntryRva,EntryOriginal(image));
        TerrainPatch.Expect(memory,image+EntryRva+5,TerrainPatch.Hex("E80C9D160051568BF18B4E04"));
        ExhaustionRecoveryPatch.Validate(memory,image);
    }
    internal static void Install(IMemory memory,uint image,Action<string> log)
    {
        Validate(memory,image);
        bool entry=false, update=false;
        try
        {
            entry=true; memory.WriteCode(image+EntryRva,EntryDisabled);
            TerrainPatch.Expect(memory,image+EntryRva,EntryDisabled); memory.Flush(image+EntryRva,5);
            update=true; memory.WriteCode(image+UpdateRva,UpdateDisabled);
            TerrainPatch.Expect(memory,image+UpdateRva,UpdateDisabled); memory.Flush(image+UpdateRva,5);
            log("COMPANY_EXHAUSTION r1; disabled; native morale, Rout and Stun preserved");
        }
        catch
        {
            // Attempt both restorations even when the first one fails.
            if(update) try { memory.WriteCode(image+UpdateRva,ExhaustionRecoveryPatch.Original); TerrainPatch.Expect(memory,image+UpdateRva,ExhaustionRecoveryPatch.Original); memory.Flush(image+UpdateRva,5); }
                catch(Exception e) { log("EXHAUSTION_UPDATE_ROLLBACK_UNCERTAIN: "+e.Message); }
            if(entry) try { memory.WriteCode(image+EntryRva,EntryOriginal(image)); TerrainPatch.Expect(memory,image+EntryRva,EntryOriginal(image)); memory.Flush(image+EntryRva,5); }
                catch(Exception e) { log("EXHAUSTION_ENTRY_ROLLBACK_UNCERTAIN: "+e.Message); }
            throw;
        }
    }
}
