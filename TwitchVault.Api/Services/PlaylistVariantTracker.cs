using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public sealed class PlaylistVariantTracker(
    string channelName,
    TwitchClient twitchClient,
    ILogger<PlaylistVariantTracker> logger)
{
    private const int MinPollsBeforeStabilization = 5;
    private const int MaxPlaylistVariantsPolls = 10;
    private const int MinVariantsForEarlyStabilization = 5;
    private static readonly TimeSpan MasterPlaylistRefreshInterval = TimeSpan.FromSeconds(30);

    private MediaPlaylist[] _playlistVariants = [];
    private bool _isStabilized;
    private int _stableVariantsCount;
    private int _totalPolls;
    private DateTime _lastRefreshedAt = DateTime.MinValue;

    private bool IsRefreshNeeded =>
        DateTime.Now - _lastRefreshedAt >= MasterPlaylistRefreshInterval;

    public async Task<MediaPlaylist[]> GetVariantsAsync(CancellationToken cancellationToken)
    {
        if (_isStabilized)
            return _playlistVariants;

        if (!IsRefreshNeeded)
            return _playlistVariants;

        await RefreshAsync(cancellationToken);
        return _playlistVariants;
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        _lastRefreshedAt = DateTime.Now;

        var masterPlaylist = await twitchClient.GetMasterPlaylistAsync(channelName, cancellationToken);
        if (string.IsNullOrWhiteSpace(masterPlaylist))
            return;

        var variants = MasterPlaylistParser.ParseVariants(masterPlaylist);
        if (variants.Length == 0)
            return;

        UpdateStabilityTracking(variants);
        _playlistVariants = variants;
        _totalPolls++;

        if (!IsStable())
            return;

        _isStabilized = true;

        logger.LogWarning(
            "Master playlist variants stabilized for {Channel}. Total variants: {Count}",
            channelName, _playlistVariants.Length);

        for (var i = 0; i < _playlistVariants.Length; i++)
            logger.LogInformation("Variant Rank {Rank}: {Bandwidth}", i + 1, _playlistVariants[i].Bandwidth);
    }

    private void UpdateStabilityTracking(MediaPlaylist[] fetched)
    {
        if (fetched.Length > _playlistVariants.Length)
            _stableVariantsCount = 0;
        else
            _stableVariantsCount++;
    }

    private bool IsStable() =>
        _totalPolls >= MinPollsBeforeStabilization &&
        (_playlistVariants.Length >= MinVariantsForEarlyStabilization || _stableVariantsCount >= MaxPlaylistVariantsPolls);
}