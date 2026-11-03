using PawsPatchLauncher;

public static class OfficialFeedConfigurationTests
{
    public static int Run()
    {
        const string key = "-----BEGIN PUBLIC KEY-----\r\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAELTU21+pvKiNCLqG44ge7vyZH5J8C\r\n3eOJEpo428Jn2eH14PLmcOHoOCxYuqxXPppg4U7Me2+E8h5WXxTltcEumQ==\r\n-----END PUBLIC KEY-----";
        var count = 0;
        void Check(bool condition) { if (!condition) throw new Exception("Official catalog upgrade failed."); count++; }
        var root = OfficialFeedConfiguration.Root;
        var c = new LauncherConfiguration { PublicKeyPem = key, FeedUrls = [root + "stable.json", "https://mirror.example/feed.json"], BetaFeedUrls = [root + "beta.json"], CacheRoot = "custom-cache" };
        OfficialFeedConfiguration.Upgrade(c);
        Check(c.FeedUrls.SequenceEqual([root + "v3/stable.json", "https://mirror.example/feed.json"]));
        Check(c.BetaFeedUrls.SequenceEqual([root + "v3/beta.json"]));
        Check(c.CacheRoot == "custom-cache" && c.PublicKeyPem == key && c.RequireSignedRemoteFeed);
        OfficialFeedConfiguration.Upgrade(c);
        Check(c.FeedUrls[0] == root + "v3/stable.json");
        var v2 = new LauncherConfiguration { PublicKeyPem = key, FeedUrls = [root + "v2/stable.json"], BetaFeedUrls = [root + "v2/beta.json"] };
        OfficialFeedConfiguration.Upgrade(v2);
        Check(v2.FeedUrls[0] == root + "v3/stable.json" && v2.BetaFeedUrls[0] == root + "v3/beta.json");
        foreach (var customKey in new[] { "", "test-key" })
        {
            var custom = new LauncherConfiguration { PublicKeyPem = customKey, FeedUrls = [root + "stable.json"] };
            Check(OfficialFeedConfiguration.Upgrade(custom).FeedUrls[0] == root + "stable.json");
        }
        var unsigned = new LauncherConfiguration { PublicKeyPem = key, RequireSignedRemoteFeed = false, FeedUrls = [root + "stable.json"] };
        Check(OfficialFeedConfiguration.Upgrade(unsigned).FeedUrls[0] == root + "stable.json");
        var local = new LauncherConfiguration { PublicKeyPem = key, FeedUrls = ["local/stable.json", root + "stable.json?preview=1"] };
        Check(OfficialFeedConfiguration.Upgrade(local).FeedUrls.SequenceEqual(["local/stable.json", root + "stable.json?preview=1"]));
        Console.WriteLine($"OFFICIAL CATALOG UPGRADE PASS {count}");
        return count;
    }
}
