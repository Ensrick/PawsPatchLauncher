using System.Security.Cryptography;

namespace PawsPatchLauncher;

internal static class SteamVanillaBaseline
{
    // These loose files belong to Steam build 25068126. Their SHA-1 values
    // match depot 97131 manifest 827975205587039857 (see Assets/Vanilla1372).
    internal static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CryptoAndIO.NormalizeRelativePath("startup/autoexec.txt")] = "628D30D3D0D8E9F4E1DD33604F6B9869FFC9B92BCA0ABC5D6B80CF5FB644666F",
        [CryptoAndIO.NormalizeRelativePath("data/UI/Shared/options_dialog.tgi")] = "D3B4C57F5C013A995AD52772E18E71757BBFDA3118B893E3647DFD0CBA41E0EA",
        [CryptoAndIO.NormalizeRelativePath("skins/Drauga.rwd")] = "1164BA91EA5FBED6D958C6F0407494F0C7C7D683F4755DACEA9065753C557F28",
        [CryptoAndIO.NormalizeRelativePath("skins/Gauri.rwd")] = "DA5E1BAE73424182F9184F77146A86EBD2B25DE03F4E804A35CDD980083EE8A0",
        [CryptoAndIO.NormalizeRelativePath("skins/Haroun.rwd")] = "7D8CC77D038AC899AD003691457E519A43F14CBC6BEFBE786FEFD9EB5E85AE37",
        [CryptoAndIO.NormalizeRelativePath("skins/Human.rwd")] = "D92375FB84D6FF76B4C75092EA9981587FD1F71E96E0CB0BF800B6A1708472B8",
        [CryptoAndIO.NormalizeRelativePath("skins/Shadow.rwd")] = "33065FD644B98B5F954B265E2330BEF328BD642EA932CCA19803BDDF9DDF5C7A",
        [CryptoAndIO.NormalizeRelativePath("skins/Undead.rwd")] = "86010014C71ACE46AA3E28AB29B55EE625B104AFE0BC57C52418487DA75E69FB"
    };

    internal static bool IsOriginal(string relative, string hash)
        => Files.TryGetValue(relative, out var expected) && hash.Equals(expected, StringComparison.OrdinalIgnoreCase);

    // Steam build 25068126 (1.3.72).
    internal const string ExecutableSha256 = "1EB79BBB678668BE5A05F8C98103CD9988048490CE7C53C74C9A1D0813E9CD45";

    internal static async Task<bool> MatchesGameAsync(string root, CancellationToken cancellationToken)
    {
        var executable = PatchRecovery.GamePath(root, "k2.exe");
        return File.Exists(executable) && (await CryptoAndIO.Sha256Async(executable, cancellationToken))
            .Equals(ExecutableSha256, StringComparison.OrdinalIgnoreCase);
    }

    internal static byte[] Read(string relative)
    {
        using var stream = typeof(SteamVanillaBaseline).Assembly.GetManifestResourceStream("PawsPatchLauncher.Assets.Vanilla1372." + Path.GetFileName(relative))
            ?? throw new InvalidDataException("The original Steam file is missing from the launcher.");
        using var buffer = new MemoryStream(); stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(Files[relative], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The original Steam file failed verification.");
        return bytes;
    }
}
