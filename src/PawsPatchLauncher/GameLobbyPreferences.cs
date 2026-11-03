using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PawsPatchLauncher;

// Kohan keeps a serialized copy of the last lobby in Preferences.rup, outside
// the installation directory. Removing mod files does not remove its kingdoms.
internal static class GameLobbyPreferences
{
    private const int MaximumSize = 16 * 1024 * 1024;
    private sealed record Chunk(string Name, byte[] Directory, byte[] Data);
    private static readonly string[] ExtendedKingdoms = [
        "paws_independent_settlements", "paws_war_barbarian", "paws_war_branch",
        "paws_war_fire_dragon", "paws_war_ice_dragon", "paws_war_storm",
        "paws_war_wolf", "paws_war_spider", "paws_war_scorpion", "paws_war_night",
        "paws_war_rhaksha", "paws_war_undead", "paws_war_slaan", "paws_war_dark_rift",
        "paws_war_human", "paws_war_haroun", "paws_war_drauga", "paws_war_gauri",
        "kingdom09", "kingdom10", "kingdom11", "kingdom12", "kingdom13", "kingdom14", "kingdom15", "kingdom16"];

    // Empty new-game launch settings serialized by one exact game executable. The chunks carry that build's version
    // stamp, so a game only ever receives the defaults its own executable wrote (Assets/Vanilla1372, Assets/Vanilla1368).
    internal sealed record LobbyDefaults(string Folder, string SingleSha256, string MultiSha256);
    internal static readonly LobbyDefaults Vanilla1372 = new("Vanilla1372",
        "4EB57219BB3938009BF3E17C3816BE70FFA28AF5F95DB9A6ACB264DF8DD60799", "BDCF0F6E11A0A1ECECEA60F5A2C95B9C573FDB75EF67379FB102D52E733E2CA4");
    internal static readonly LobbyDefaults Vanilla1368 = new("Vanilla1368",
        "53AD4CD6CE73A453F2881835B064112021D9097B2BB96B8B6C2C1E8F42A0DB04", "B8D4D3635060266C64C1C13904870FD71D4F32BD643549C9EB9F338CDACE3F95");
    // Kohan II 1.3.68, the Steam public build 24805354.
    internal const string Steam1368ExecutableSha256 = "CB64676391EDF1E4677B79038A2C219B11B5E1F4D4C75D96AD9E74A7AFB9DF7A";

    // Defaults for the game executable with this SHA-256; null for any other build (its preferences stay untouched).
    internal static LobbyDefaults? DefaultsFor(string executableSha256)
        => executableSha256.Equals(SteamVanillaBaseline.ExecutableSha256, StringComparison.OrdinalIgnoreCase) ? Vanilla1372
            : executableSha256.Equals(Steam1368ExecutableSha256, StringComparison.OrdinalIgnoreCase) ? Vanilla1368 : null;

    internal static byte[]? Repair(byte[] bytes, UserSettings settings) => Repair(bytes, settings, Vanilla1372);

    internal static byte[]? Repair(byte[] bytes, UserSettings settings, LobbyDefaults defaults)
    {
        if (GameMod.IsArcaneWars(settings) && settings.PawPatchEnabled && !settings.DataOnly) return null;
        var chunks = Read(bytes);
        // Unknown/newer game formats must remain untouched.
        if (chunks is null) return null;
        var changed = false;
        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            if (chunk.Name is not ("SingleLaunchSettings" or "MultiLaunchSettings")) continue;
            if (!ExtendedKingdoms.Any(id => ContainsString(chunk.Data, id))) continue;
            chunks[i] = chunk with { Data = Default(chunk.Name, defaults) };
            changed = true;
        }
        if (!changed) return null;
        // The map-generator cache belongs to these launch settings. Keep its
        // required chunk, using the game's empty dictionary representation.
        for (var i = 0; i < chunks.Count; i++)
            if (chunks[i].Name == "RMCPreferences") chunks[i] = chunks[i] with { Data = new byte[4] };
        using var result = new MemoryStream();
        result.Write(bytes, 0, 12);
        foreach (var chunk in chunks)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(chunk.Directory.AsSpan(64), (ulong)result.Position);
            BinaryPrimitives.WriteUInt64LittleEndian(chunk.Directory.AsSpan(72), (ulong)chunk.Data.Length);
            BinaryPrimitives.WriteUInt64LittleEndian(chunk.Directory.AsSpan(88), (ulong)chunk.Data.Length);
            result.Write(chunk.Data);
        }
        foreach (var chunk in chunks) result.Write(chunk.Directory);
        return result.ToArray();
    }

    private static bool ContainsString(byte[] data, string value)
    {
        var serialized = new byte[2 + value.Length * 2];
        BinaryPrimitives.WriteUInt16LittleEndian(serialized, (ushort)value.Length);
        Encoding.Unicode.GetBytes(value, serialized.AsSpan(2));
        return data.AsSpan().IndexOf(serialized) >= 0;
    }

    private static List<Chunk>? Read(byte[] bytes)
    {
        if (bytes.Length is < 12 or > MaximumSize || !bytes.AsSpan(0, 4).SequenceEqual("TGCK"u8)
            || BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(4)) != 2) return null;
        var count = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8));
        if (count is < 1 or > 64 || count * 96 > bytes.Length - 12) return null;
        var start = bytes.Length - count * 96;
        var chunks = new List<Chunk>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var ranges = new List<(ulong Start, ulong End)>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(start + i * 96, 96).ToArray();
            var name = Encoding.Unicode.GetString(entry, 0, 64).TrimEnd('\0');
            var offset = BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(64));
            var length = BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(72));
            if (name.Length == 0 || name.Contains('\0') || !names.Add(name)
                || offset < 12 || offset > (ulong)start || length > (ulong)start - offset
                || BinaryPrimitives.ReadUInt64LittleEndian(entry.AsSpan(88)) != length
                || ranges.Any(r => offset < r.End && offset + length > r.Start)) return null;
            ranges.Add((offset, offset + length));
            chunks.Add(new(name, entry, bytes.AsSpan((int)offset, (int)length).ToArray()));
        }
        var header = chunks.SingleOrDefault(c => c.Name == "HeaderInfo");
        if (header?.Data.Length != 4 || BinaryPrimitives.ReadInt32LittleEndian(header.Data) != 19
            || !names.Contains("SimplePreferences") || !names.Contains("SingleLaunchSettings")
            || !names.Contains("MultiLaunchSettings") || !names.Contains("RMCPreferences")) return null;
        return chunks;
    }

    internal static byte[] Default(string name, LobbyDefaults defaults)
    {
        using var stream = typeof(GameLobbyPreferences).Assembly.GetManifestResourceStream("PawsPatchLauncher.Assets." + defaults.Folder + "." + name + ".bin")
            ?? throw new InvalidDataException("Missing default lobby settings.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var expected = name == "SingleLaunchSettings" ? defaults.SingleSha256 : defaults.MultiSha256;
        if (Convert.ToHexString(SHA256.HashData(bytes)) != expected) throw new InvalidDataException("Default lobby settings failed verification.");
        return bytes;
    }

    internal static Task<bool> PrepareAsync(string path, UserSettings settings) => PrepareAsync(path, settings, Vanilla1372);

    internal static async Task<bool> PrepareAsync(string path, UserSettings settings, LobbyDefaults defaults)
    {
        if (!File.Exists(path)) return false;
        RemovalSafety.CheckNoLinks(path);
        if (new FileInfo(path).Length > MaximumSize) return false;
        // The caller ensures no game is running; the second comparison also
        // refuses to overwrite a file changed while the backup was prepared.
        var before = await File.ReadAllBytesAsync(path);
        var after = Repair(before, settings, defaults);
        if (after is null) return false;
        var backupDirectory = Path.Combine(Path.GetDirectoryName(path)!, "PawsLauncherBackups");
        RemovalSafety.CheckNoLinks(backupDirectory);
        Directory.CreateDirectory(backupDirectory);
        var backup = Path.Combine(backupDirectory, "Preferences-" + Convert.ToHexString(SHA256.HashData(before)) + ".rup");
        if (!File.Exists(backup)) await File.WriteAllBytesAsync(backup, before);
        if (!(await File.ReadAllBytesAsync(backup)).SequenceEqual(before)) throw new IOException("Lobby preferences backup failed verification.");
        var temporary = path + ".pawpatch.tmp";
        await File.WriteAllBytesAsync(temporary, after);
        if (!(await File.ReadAllBytesAsync(path)).SequenceEqual(before)) throw new IOException("Game preferences changed during preparation.");
        File.Move(temporary, path, true);
        ActionJournal.Record("game.lobby-preferences.repaired", settings.Mod);
        return true;
    }

    internal static async Task PrepareForLaunchAsync(string game, UserSettings settings)
    {
        if (ActivityStore.IsSmokeTest) return;
        await PrepareForLaunchAsync(game, settings, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
    }

    // Explicit root keeps integration fixtures out of the player's Documents.
    internal static async Task PrepareForLaunchAsync(string game, UserSettings settings, string documents)
    {
        var defaults = await DefaultsForGameAsync(game, default);
        if (defaults is null) return;
        await PrepareAsync(Path.Combine(documents, "Kohan2", "data", "User", "Preferences.rup"), settings, defaults);
        await PrepareAsync(Path.Combine(game, "Userdata", "User", "Preferences.rup"), settings, defaults);
    }

    // The same executable check as SteamVanillaBaseline.MatchesGameAsync for 1.3.72, plus the 1.3.68 public build with
    // its own defaults; any other executable is left alone.
    internal static async Task<LobbyDefaults?> DefaultsForGameAsync(string game, CancellationToken cancellationToken)
    {
        var executable = PatchRecovery.GamePath(game, "k2.exe");
        return File.Exists(executable) ? DefaultsFor(await CryptoAndIO.Sha256Async(executable, cancellationToken)) : null;
    }
}
