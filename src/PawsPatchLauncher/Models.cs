using System.Text.Json.Serialization;

namespace PawsPatchLauncher;

public sealed class LauncherConfiguration
{
    public List<string> FeedUrls { get; set; } = [];
    public List<string> BetaFeedUrls { get; set; } = ["https://raw.githubusercontent.com/VasiliyPaw/PawsPatchLauncher/main/feed/beta.json"];
    public string PublicKeyPem { get; set; } = "";
    public string? CacheRoot { get; set; }
    public bool RequireSignedRemoteFeed { get; set; } = true;
    public string SteamAppId { get; set; } = "97130";
    public string PreferredGameExecutable { get; set; } = "k2_paws_family_herd_relations_1372.exe";
}

public sealed class UserSettings
{
    public string Mod { get; set; } = GameMod.ArcaneWars;
    public bool PawPatchEnabled { get; set; } = true;
    public ArcaneComponentSelection? SuspendedArcaneComponents { get; set; }
    public bool VanillaPawPatchEnabled { get; set; }
    public bool ImmortalsPawPatchEnabled { get; set; }
    public PureComponentSelection VanillaComponents { get; set; } = new();
    public PureComponentSelection ImmortalsComponents { get; set; } = new();
    public bool ModNoticeSeen { get; set; }
    public bool DataOnly { get; set; }
    public string CompatibilityNoticeRead { get; set; } = "";
    public bool NotificationSoundEnabled { get; set; } = true;
    public bool ShareGameActivity { get; set; } = true;
    public double CommunityWidth { get; set; }
    public string CommunityChannel { get; set; } = "";
    public string CommunityNotifications { get; set; } = "mentions";
    public Dictionary<string, long> CommunityRead { get; set; } = new();
    public int NotificationVolume { get; set; } = 100;
    public string NotificationSoundName { get; set; } = "";
    public string Language { get; set; } = "ru";
    public string? GamePath { get; set; }
    public string Channel { get; set; } = "stable";
    public Dictionary<string, ModChannelPreference> ModChannels { get; set; } = new();
    public List<string> BetaNoticesSeen { get; set; } = [];
    public bool RussianLocalization { get; set; } = true;
    public string? GameTextLanguage { get; set; }
    public string? GameVoiceLanguage { get; set; }
    public bool CustomPlayerColors { get; set; }
    public string DesyncMode { get; set; } = "official";
    public bool PureBetaFeatures { get; set; }
    public bool IndependentHostility { get; set; } = true;
    public string RoamingSpawnMode { get; set; } = "x4";
    public bool AdditionalRoamingCompanies { get; set; } = true;
    public bool SiegeBalance { get; set; } = true;
    public bool ImprovedAi { get; set; } = true;
    public bool DisablePowersAndShards { get; set; } = true;
    public bool LargeMapSizes { get; set; } = true;
    public string? PreparedChannel { get; set; }
    public string? PreparedFeedFingerprint { get; set; }
    public string? PinnedRelease { get; set; }
    public Dictionary<string, string> ReadChangelogs { get; set; } = new();
}

public sealed class SignedFeedEnvelope
{
    public string KeyId { get; set; } = "";
    public string Payload { get; set; } = "";
    public string Signature { get; set; } = "";
}

public sealed class ChannelManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Channel { get; set; } = "stable";
    public string PublishedAt { get; set; } = "";
    public LauncherRelease Launcher { get; set; } = new();
    public GameRequirement Game { get; set; } = new();
    public Dictionary<string, GameRequirement> ModGames { get; set; } = new();
    public List<PackageRelease> Packages { get; set; } = [];
    public LocalizedText NewsTitle { get; set; } = new();
    public LocalizedText NewsBody { get; set; } = new();
    public List<ChangelogEntry> Changelog { get; set; } = [];
    public List<ReleaseReference> PreviousReleases { get; set; } = [];
    public bool ColorDesyncContinue { get; set; }
    public bool IndependentColorHostility { get; set; }
    public bool PureRuntimeOptions { get; set; }
    public bool PureLairRecoveryBuiltIn { get; set; }
    public int PlayerColorCount { get; set; } = PawsPatchLauncher.PatchGuide.PlayerColorCount;
    public PatchGuideDocument? PatchGuide { get; set; }
    public List<ModGuideDocument> ModGuides { get; set; } = [];
}

public sealed class ModGuideDocument
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public LocalizedText Description { get; set; } = new();
    public List<ModGuideSection> Sections { get; set; } = [];
    public List<ChangelogEntry> Changelog { get; set; } = [];
    public PatchGuideDocument? PatchGuide { get; set; }
}

public sealed class ModGuideSection
{
    public string Id { get; set; } = "";
    public LocalizedText Title { get; set; } = new();
    public LocalizedText Body { get; set; } = new();
}

public sealed class PatchGuideDocument
{
    public int SchemaVersion { get; set; } = 1;
    public string Version { get; set; } = "";
    public List<PatchGuideEntry> Entries { get; set; } = [];
}

public sealed class ReleaseReference
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class ChangelogEntry
{
    public List<string> Mods { get; set; } = [];
    public string Category { get; set; } = "patch";
    public string Version { get; set; } = "";
    public string PublishedAt { get; set; } = "";
    public LocalizedText Title { get; set; } = new();
    public LocalizedText Body { get; set; } = new();
}

public sealed class LauncherRelease
{
    public string Version { get; set; } = "0.0.0";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public List<string> Urls { get; set; } = [];
}

public sealed class GameRequirement
{
    public string Version { get; set; } = "1.3.72";
    public string SteamBuild { get; set; } = "25068126";
    public List<string> K2ExeSha256 { get; set; } = [];
}

public sealed class PackageRelease
{
    public List<string> Mods { get; set; } = [];
    public bool ExecutableIndependent { get; set; }
    public string Id { get; set; } = "";
    public string Version { get; set; } = "0.0.0";
    public int Priority { get; set; }
    public bool Required { get; set; }
    public bool Experimental { get; set; }
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public List<string> Urls { get; set; } = [];
    public List<string> DependsOn { get; set; } = [];
    public LocalizedText Name { get; set; } = new();
    public LocalizedText Description { get; set; } = new();
}

public sealed class LocalizedText
{
    public string Ru { get; set; } = "";
    public string En { get; set; } = "";
    public string Get(string language) => language.Equals("ru", StringComparison.OrdinalIgnoreCase) ? Ru : En;
}

public sealed class ModuleArchiveManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public List<ModuleFile> Files { get; set; } = [];
    public List<string> Remove { get; set; } = [];
}

public sealed class ModuleFile
{
    public string Path { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long Size { get; set; }
}

public sealed class InstallState
{
    public Dictionary<string, List<CustomRwdMod>> CustomMods { get; set; } = new(StringComparer.Ordinal);
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, InstalledModule> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, OriginalFile> Originals { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string LastSuccessfulUpdate { get; set; } = "";
    public UserSettings? AppliedSettings { get; set; }
    public string? ReleaseId { get; set; }
    public GameRequirement? GameRequirement { get; set; }
    public string? BaseGameSha256 { get; set; }
}

public sealed class InstalledModule
{
    public string Version { get; set; } = "";
    public DateTimeOffset? DownloadedAt { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; }
    public string ArchiveSha256 { get; set; } = "";
    public List<ModuleFile> Files { get; set; } = [];
    public List<string> Remove { get; set; } = [];
}

public sealed class OriginalFile
{
    public bool Existed { get; set; }
    public string? BackupRelativePath { get; set; }
    public string? Sha256 { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(LauncherConfiguration))]
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(SignedFeedEnvelope))]
[JsonSerializable(typeof(ChannelManifest))]
[JsonSerializable(typeof(ModuleArchiveManifest))]
[JsonSerializable(typeof(InstallState))]
[JsonSerializable(typeof(PatchTransaction))]
[JsonSerializable(typeof(RunRecord))]
[JsonSerializable(typeof(LastWorkingConfiguration))]
[JsonSerializable(typeof(MultiplayerManifest))]
[JsonSerializable(typeof(DiagnosticArchiveReference))]
[JsonSerializable(typeof(SavedWindowPlacement))]
[JsonSerializable(typeof(List<ModGuideDocument>))]
[JsonSerializable(typeof(ModLibraryDocument))]
public partial class LauncherJsonContext : JsonSerializerContext;
