namespace PawsPatchLauncher;

public static class OfficialFeedConfiguration
{
    public const string Root = "https://raw.githubusercontent.com/VasiliyPaw/PawsPatchLauncher/main/feed/";
    private const string Key = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAELTU21+pvKiNCLqG44ge7vyZH5J8C3eOJEpo428Jn2eH14PLmcOHoOCxYuqxXPppg4U7Me2+E8h5WXxTltcEumQ==";

    public static LauncherConfiguration Upgrade(LauncherConfiguration configuration)
    {
        // Older ZIP installations keep their sidecar when the EXE updates.
        // Move only the signed official endpoints; custom feeds keep their identity.
        var key = string.Concat(configuration.PublicKeyPem.Replace("-----BEGIN PUBLIC KEY-----", "")
            .Replace("-----END PUBLIC KEY-----", "").Where(c => !char.IsWhiteSpace(c)));
        if (!configuration.RequireSignedRemoteFeed || key != Key) return configuration;
        configuration.FeedUrls = UpgradeSources(configuration.FeedUrls);
        configuration.BetaFeedUrls = UpgradeSources(configuration.BetaFeedUrls);
        return configuration;
    }

    private static List<string> UpgradeSources(List<string> sources) => sources.Select(source => source switch
    {
        Root + "stable.json" or Root + "v2/stable.json" => Root + "v3/stable.json",
        Root + "beta.json" or Root + "v2/beta.json" => Root + "v3/beta.json",
        _ => source
    }).ToList();
}
