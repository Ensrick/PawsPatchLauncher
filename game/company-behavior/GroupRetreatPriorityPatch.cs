using System;

// An explicit Run order must never become group combat support.
internal static class GroupRetreatPriorityPatch
{
    internal const uint HookRva = 0x233035;
    internal static readonly byte[] Original = TerrainPatch.Hex("515153558BE9");
    internal static readonly byte[] Guard = TerrainPatch.Hex("515153558BE95633F6578B85940100008944241485C07E54");
    // Native command kind 7 is explicit Run. Preserve all state on the native path.
    internal const string CodeHex = "9c508b81ac01000085c074128b400885c0740b83780c077505589db000c3589d5151535589cde9103069a0";
    internal static byte[] Build(uint image, uint cave)
    {
        byte[] code = TerrainPatch.Hex(CodeHex);
        Array.Copy(BitConverter.GetBytes(unchecked(image + 0x23303b - (cave + (uint)code.Length))), 0, code, code.Length-4, 4);
        return code;
    }
    internal static void Validate(IMemory memory, uint image)
    {
        TerrainPatch.Expect(memory, image + HookRva, Guard);
    }
    internal static byte[] Jump(uint image, uint cave)
    {
        byte[] hook = new byte[] { 0xe9, 0, 0, 0, 0, 0x90 };
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
            log("GROUP_RETREAT_PRIORITY r1; explicit Run keeps priority over group combat support; image=0x" + image.ToString("X8") + " cave=0x" + cave.ToString("X8"));
            return cave;
        }
        catch
        {
            if (attempted)
            {
                try { memory.WriteCode(image + HookRva, Original); TerrainPatch.Expect(memory, image + HookRva, Original); memory.Flush(image + HookRva, Original.Length); safeToFree = true; }
                catch (Exception e) { log("GROUP_RETREAT_PRIORITY_ROLLBACK_UNCERTAIN: " + e.Message); }
            }
            if (cave != 0 && safeToFree) { try { memory.Free(cave); } catch (Exception e) { log("GROUP_RETREAT_PRIORITY_FREE_FAILED: " + e.Message); } }
            throw;
        }
    }
}
