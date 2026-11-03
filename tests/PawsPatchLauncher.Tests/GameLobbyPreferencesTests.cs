using System.Buffers.Binary;
using System.Text;
using PawsPatchLauncher;

internal static class GameLobbyPreferencesTests
{
    private static Dictionary<string, byte[]> Chunks(byte[] bytes)
    {
        var count = BitConverter.ToInt32(bytes, 8);
        var result = new Dictionary<string, byte[]>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(bytes.Length - count * 96 + i * 96, 96);
            var name = Encoding.Unicode.GetString(entry[..64]).TrimEnd('\0');
            result[name] = bytes.AsSpan((int)BinaryPrimitives.ReadUInt64LittleEndian(entry[64..]), (int)BinaryPrimitives.ReadUInt64LittleEndian(entry[72..])).ToArray();
        }
        return result;
    }
    private static byte[] String(string value)
    {
        using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
        writer.Write((ushort)value.Length); writer.Write(Encoding.Unicode.GetBytes(value));
        return data.ToArray();
    }
    private static bool Has(byte[] data, string text) => data.AsSpan().IndexOf(Encoding.Unicode.GetBytes(text)) >= 0;
    private static byte[] Fixture(bool single, bool multi) => Build(new Dictionary<string, byte[]> {
            ["HeaderInfo"] = BitConverter.GetBytes(19), ["SimplePreferences"] = Encoding.UTF8.GetBytes("private name and general preferences must survive"),
            ["SingleLaunchSettings"] = String(single ? "kingdom16" : "kingdom01"),
            ["MultiLaunchSettings"] = String(multi ? "paws_war_barbarian" : "kingdom08"),
            ["RMCPreferences"] = [1,2,3,4], ["UnknownExtension"] = [7,6,5,4,3] });
    private static byte[] Build(Dictionary<string, byte[]> chunks)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("TGCK"u8); writer.Write(2); writer.Write(chunks.Count);
        var entries = new List<byte[]>();
        foreach (var (name, data) in chunks)
        {
            var entry = new byte[96]; Encoding.Unicode.GetBytes(name).CopyTo(entry, 0);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(64), (ulong)stream.Position);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(72), (ulong)data.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(80), 0x01043C020A6C6C88);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.AsSpan(88), (ulong)data.Length);
            entries.Add(entry); writer.Write(data);
        }
        foreach (var entry in entries) writer.Write(entry);
        return stream.ToArray();
    }
    internal static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
        foreach (var defaults in new[] { GameLobbyPreferences.Vanilla1372, GameLobbyPreferences.Vanilla1368 })
        foreach (var mod in new[] { GameMod.Vanilla, GameMod.Immortals, GameMod.ArcaneWars })
        foreach (var paw in new[] { false, true })
        foreach (var dataOnly in new[] { false, true })
        foreach (var single in new[] { false, true })
        foreach (var multi in new[] { false, true })
        {
            var input = Fixture(single, multi); var copy = input.ToArray();
            var settings = new UserSettings { Mod = mod, PawPatchEnabled = paw, DataOnly = dataOnly };
            var output = GameLobbyPreferences.Repair(input, settings, defaults);
            var shouldRepair = (single || multi) && !(mod == GameMod.ArcaneWars && paw && !dataOnly);
            Check((output is not null) == shouldRepair, "Incorrect compatibility decision");
            Check(input.SequenceEqual(copy), "Input mutated");
            if (output is null) continue;
            var before = Chunks(input); var after = Chunks(output);
            Check(before.Keys.SequenceEqual(after.Keys), "Required chunk removed or reordered");
            foreach (var name in before.Keys.Except(new[] { "RMCPreferences", single ? "SingleLaunchSettings" : "", multi ? "MultiLaunchSettings" : "" }))
                Check(before[name].SequenceEqual(after[name]), "Unrelated preference data modified: " + name);
            Check(after["RMCPreferences"].SequenceEqual(new byte[4]), "Map cache not cleared");
            foreach (var name in new[] { "SingleLaunchSettings", "MultiLaunchSettings" })
                if (name == "SingleLaunchSettings" ? single : multi)
                    Check(after[name].SequenceEqual(GameLobbyPreferences.Default(name, defaults)), "Wrong build defaults: " + name);
            Check(GameLobbyPreferences.Repair(output, settings, defaults) is null, "Repeated launch resets preferences again");
        }
        var badInputs = new List<byte[]> { Array.Empty<byte>(), new byte[12], Fixture(true,true)[..^1] };
        foreach (var offset in new[] { 0,4,8,12 }) {var bytes=Fixture(true,true);bytes[offset]=255;badInputs.Add(bytes);}
        var overlap=Fixture(true,true);var start=overlap.Length-6*96;BinaryPrimitives.WriteUInt64LittleEndian(overlap.AsSpan(start+96+64),12);badInputs.Add(overlap);
        var compressed=Fixture(true,true);compressed[^8]=99;badInputs.Add(compressed);
        foreach(var bytes in badInputs) Check(GameLobbyPreferences.Repair(bytes,new UserSettings{Mod=GameMod.Vanilla}) is null,"Unrecognized archive modified");
        var root=Path.Combine(Path.GetTempPath(),"PawsLobbyPreferences",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"Preferences.rup");var original=Fixture(false,true);File.WriteAllBytes(path,original);
        var vanilla=new UserSettings{Mod=GameMod.Vanilla};
        Check(await GameLobbyPreferences.PrepareAsync(path,vanilla),"Disk repair skipped");
        var backup=Directory.GetFiles(Path.Combine(root,"PawsLauncherBackups")).Single();
        Check(File.ReadAllBytes(backup).SequenceEqual(original),"Backup differs");
        Check(!await GameLobbyPreferences.PrepareAsync(path,vanilla),"Disk repair is not idempotent");
        Check(Directory.GetFiles(Path.Combine(root,"PawsLauncherBackups")).Length==1,"Duplicate backups");

        // Each game build gets the defaults its own executable serialized (they carry its version stamp).
        var v72 = GameLobbyPreferences.Vanilla1372; var v68 = GameLobbyPreferences.Vanilla1368;
        Check(GameLobbyPreferences.DefaultsFor(SteamVanillaBaseline.ExecutableSha256) == v72, "1.3.72 executable did not select its defaults");
        Check(GameLobbyPreferences.DefaultsFor(GameLobbyPreferences.Steam1368ExecutableSha256.ToLowerInvariant()) == v68, "1.3.68 executable did not select its defaults");
        Check(GameLobbyPreferences.DefaultsFor(new string('0', 64)) is null, "Unknown executable selected defaults");
        var single72 = GameLobbyPreferences.Default("SingleLaunchSettings", v72); var multi72 = GameLobbyPreferences.Default("MultiLaunchSettings", v72);
        var single68 = GameLobbyPreferences.Default("SingleLaunchSettings", v68); var multi68 = GameLobbyPreferences.Default("MultiLaunchSettings", v68);
        Check(Has(single68, "1.3.68") && Has(multi68, "1.3.68"), "1.3.68 defaults lack their version stamp");
        Check(Has(single72, "1.3.72") && Has(multi72, "1.3.72"), "1.3.72 defaults lack their version stamp");
        Check(!single68.SequenceEqual(single72) && !multi68.SequenceEqual(multi72), "Builds share one default");
        // A 1.3.68 lobby left by a 16-kingdom game: its own defaults plus kingdom09..16 in single player, multiplayer clean.
        var leftover = single68.Concat(Enumerable.Range(9, 8).SelectMany(i => String($"kingdom{i:00}"))).ToArray();
        var simple = Encoding.UTF8.GetBytes("general preferences of a 1.3.68 player must survive");
        var lobby68 = Build(new Dictionary<string, byte[]> {
            ["HeaderInfo"] = BitConverter.GetBytes(19), ["SimplePreferences"] = simple, ["SingleLaunchSettings"] = leftover,
            ["MultiLaunchSettings"] = multi68, ["RMCPreferences"] = [1,2,3,4] });
        var repaired68 = GameLobbyPreferences.Repair(lobby68, new UserSettings { Mod = GameMod.Vanilla }, v68);
        Check(repaired68 is not null, "1.3.68 lobby with kingdom09..16 not repaired");
        var after68 = Chunks(repaired68!);
        Check(after68["SingleLaunchSettings"].SequenceEqual(single68), "1.3.68 lobby did not get the 1.3.68 single-player defaults");
        Check(after68["MultiLaunchSettings"].SequenceEqual(multi68), "Clean 1.3.68 multiplayer lobby changed");
        Check(after68["SimplePreferences"].SequenceEqual(simple) && after68["HeaderInfo"].SequenceEqual(BitConverter.GetBytes(19)), "1.3.68 general preferences changed");
        Check(after68["RMCPreferences"].SequenceEqual(new byte[4]), "1.3.68 map cache not cleared");
        Check(GameLobbyPreferences.Repair(repaired68!, new UserSettings { Mod = GameMod.Vanilla }, v68) is null, "Repaired 1.3.68 lobby repaired again");
        Check(Chunks(GameLobbyPreferences.Repair(lobby68, new UserSettings { Mod = GameMod.Vanilla })!)["SingleLaunchSettings"].SequenceEqual(single72), "Default repair no longer uses the 1.3.72 defaults");
        var path68 = Path.Combine(root, "Preferences-1368.rup"); File.WriteAllBytes(path68, lobby68);
        Check(await GameLobbyPreferences.PrepareAsync(path68, vanilla, v68), "1.3.68 disk repair skipped");
        Check(File.ReadAllBytes(path68).SequenceEqual(repaired68!), "1.3.68 disk repair differs from the in-memory repair");
        Check(Directory.GetFiles(Path.Combine(root, "PawsLauncherBackups")).Any(f => File.ReadAllBytes(f).SequenceEqual(lobby68)), "1.3.68 backup missing");
        // Game folders: no executable or an unknown one never selects defaults (its preferences stay untouched).
        var unknownGame = Path.Combine(root, "unknown-game"); Directory.CreateDirectory(unknownGame);
        Check(await GameLobbyPreferences.DefaultsForGameAsync(unknownGame, default) is null, "Game without k2.exe selected defaults");
        File.WriteAllBytes(Path.Combine(unknownGame, "k2.exe"), [0x4d, 0x5a, 1, 2, 3]);
        Check(await GameLobbyPreferences.DefaultsForGameAsync(unknownGame, default) is null, "Unknown k2.exe selected defaults");
        var documents = Path.Combine(root, "isolated-documents");
        var unknownPreferences = Path.Combine(unknownGame, "Userdata", "User", "Preferences.rup");
        Directory.CreateDirectory(Path.GetDirectoryName(unknownPreferences)!);
        File.WriteAllBytes(unknownPreferences, lobby68);
        await GameLobbyPreferences.PrepareForLaunchAsync(unknownGame, vanilla, documents);
        Check(File.ReadAllBytes(unknownPreferences).SequenceEqual(lobby68), "Unknown game preferences changed through launch path");
        Check(!Directory.Exists(documents), "Unknown game touched Documents");
        Console.WriteLine($"LOBBY PREFERENCES PASS {checks}: mode/component transitions, both launch caches, general preferences, unknown chunks/formats, backup and repeated launch, per-build defaults (1.3.72, 1.3.68, unknown).");
        return checks;
    }
    internal static async Task RepairFixtureAsync(string input, string output)
    {
        if(File.Exists(output)) throw new IOException("Choose an unused output file.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.Copy(input,output);
        var before=Chunks(File.ReadAllBytes(input));
        if(!await GameLobbyPreferences.PrepareAsync(output,new UserSettings{Mod=GameMod.Vanilla})) throw new Exception("Crash fixture did not trigger guard");
        var after=Chunks(File.ReadAllBytes(output));
        foreach(var name in new[]{"HeaderInfo","SimplePreferences","SingleLaunchSettings"})
            if(!before[name].SequenceEqual(after[name])) throw new Exception("Unrelated crash fixture chunk changed: "+name);
        Console.WriteLine("CRASH-267 FIXTURE PASS: only MultiLaunchSettings and RMCPreferences reset; original file untouched.");
    }
}
