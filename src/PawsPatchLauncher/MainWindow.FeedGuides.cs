namespace PawsPatchLauncher;

public partial class MainWindow
{
    private readonly Dictionary<string, ChannelManifest?> _cachedGuideChannels = new();

    // The feed release whose guides and feature flags describe the current selection (component help texts and
    // feature availability read it).
    private ChannelManifest? GuideChannel()
    {
        // Installing a cached mod does not pin its documentation. Keep normal
        // guides current while an explicit pinned release retains its archive.
        if (_settings.PinnedRelease is null && _latestChannel is not null
            && _latestChannel.Channel.Equals(_settings.Channel, StringComparison.OrdinalIgnoreCase)) return _latestChannel;
        if (_channel is not null && MatchesGuideSelection(_channel)) return _channel;
        var key = _settings.Channel + ":" + _settings.PinnedRelease;
        if (!_cachedGuideChannels.TryGetValue(key, out var cached))
            _cachedGuideChannels[key] = cached = _feedClient.CachedGuideChannel(_settings.Channel, _settings.PinnedRelease);
        return cached ?? (_settings.PinnedRelease is not null ? new ChannelManifest { Channel = _settings.Channel } : null);
    }

    private bool MatchesGuideSelection(ChannelManifest channel)
        => channel.Channel.Equals(_settings.Channel, StringComparison.OrdinalIgnoreCase)
            && (_settings.PinnedRelease is null || ChannelFingerprint.Create(channel)
                .Equals(_settings.PinnedRelease, StringComparison.OrdinalIgnoreCase));

    private void RememberGuideChannel()
    {
        if (_channel is not null && MatchesGuideSelection(_channel))
            _cachedGuideChannels[_settings.Channel + ":" + _settings.PinnedRelease] = _channel;
    }
}
