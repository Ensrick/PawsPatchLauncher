using System;

// GGroup::HasCombat: include an out-of-cohort member only for the group's
// same explicit attack target. Native combat and Guard checks still decide.
internal static class GroupAttackRecoveryPatch
{
    internal const uint HookRva = 0x233063;
    internal static readonly byte[] Original = TerrainPatch.Hex("807C390D00742D");
    internal static readonly byte[] Guard = TerrainPatch.Hex("8BD885DB743A8B8D90010000807C390D00742D8D83000100006A006A108BC8");
    // EBP=group, EBX=member, ECX=member array, EDI=entry offset.
    // EAX/EDX are preserved; no state writes, native calls, clocks or RNG.
    internal const string CodeHex = "807c390d007559505283bdc00100000175538b85ac01000085c074498b5008837a0c0575408b500c83e24183fa4175358b401085c0742e8b537085d274278b522085d274203b4210751b8b420883780c0575128b420c83e04183f84175075a58e9053069a05a58e92b3069a0";
    internal static byte[] Build(uint image, uint cave)
    {
        byte[] code = TerrainPatch.Hex(CodeHex);
        Array.Copy(BitConverter.GetBytes(unchecked(image + 0x23306a - (cave + 0x61 + 4))), 0, code, 0x61, 4);
        Array.Copy(BitConverter.GetBytes(unchecked(image + 0x233097 - (cave + 0x68 + 4))), 0, code, 0x68, 4);
        return code;
    }
    internal static void Validate(IMemory memory, uint image)
    {
        TerrainPatch.Expect(memory, image + HookRva - 12, Guard);
    }
    internal static byte[] Jump(uint image, uint cave)
    {
        byte[] hook = new byte[] { 0xe9, 0, 0, 0, 0, 0x90, 0x90 };
        Array.Copy(BitConverter.GetBytes(unchecked(cave - (image + HookRva + 5))), 0, hook, 1, 4);
        return hook;
    }
    internal static uint Install(IMemory memory, uint image, Action<string> log)
    {
        Validate(memory, image);
        uint cave = 0; bool attempted = false, safeToFree = true;
        try
        {
            cave = memory.Allocate(4096);
            byte[] code = Build(image, cave);
            memory.Write(cave, code); TerrainPatch.Expect(memory, cave, code);
            memory.MakeExecutable(cave, 4096); memory.Flush(cave, code.Length);
            byte[] hook = Jump(image, cave); attempted = true; safeToFree = false;
            memory.WriteCode(image + HookRva, hook); TerrainPatch.Expect(memory, image + HookRva, hook);
            memory.Flush(image + HookRva, hook.Length);
            log("GROUP_ATTACK_RECOVERY r1; same-target combat cohort gate; image=0x" + image.ToString("X8") + " cave=0x" + cave.ToString("X8"));
            return cave;
        }
        catch
        {
            if (attempted)
            {
                try { memory.WriteCode(image + HookRva, Original); TerrainPatch.Expect(memory, image + HookRva, Original); memory.Flush(image + HookRva, Original.Length); safeToFree = true; }
                catch (Exception e) { log("GROUP_ATTACK_RECOVERY_ROLLBACK_UNCERTAIN: " + e.Message); }
            }
            if (cave != 0 && safeToFree) { try { memory.Free(cave); } catch (Exception e) { log("GROUP_ATTACK_RECOVERY_FREE_FAILED: " + e.Message); } }
            throw;
        }
    }
}
