using TwitchVault.Api.Twitch;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public sealed class PlaylistVariantTracker(
    string channelName,
    ITwitchGqlClient twitchGqlClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<PlaylistVariantTracker> logger)
{
    private const int MinPollsThreshold = 5;
    private const int MaxUnchangedPollsThreshold = 10;
    private const int EarlyStabilityVariantCount = 5;
    private static readonly TimeSpan MasterPlaylistRefreshInterval = TimeSpan.FromSeconds(30);

    private MediaPlaylist[] _playlistVariants = [];
    private bool _isStabilized;
    private int _consecutiveUnchangedPolls;
    private int _totalPollCount;
    private DateTime _lastRefreshedAt = DateTime.MinValue;
    private bool IsRefreshNeeded =>
        dateTimeProvider.DateTimeNow - _lastRefreshedAt >= MasterPlaylistRefreshInterval;

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
        _lastRefreshedAt = dateTimeProvider.DateTimeNow;
        var masterPlaylist = await twitchGqlClient.GetMasterPlaylistAsync(channelName, cancellationToken);
        if (string.IsNullOrWhiteSpace(masterPlaylist))
            return;

        var variants = MasterPlaylistParser.ParseVariants(masterPlaylist);
        if (variants.Length == 0)
            return;

        UpdateStabilityTracking(variants);
        _playlistVariants = variants;
        _totalPollCount++;
        if (!IsStable())
            return;

        _isStabilized = true;
        logger.LogInformation(
            "Master playlist stabilized with {Count} quality variants. (Source: {Bandwidth} bps)",
            _playlistVariants.Length, _playlistVariants[^1].Bandwidth);
    }

    private void UpdateStabilityTracking(MediaPlaylist[] fetched)
    {
        if (fetched.Length > _playlistVariants.Length)
            _consecutiveUnchangedPolls = 0;
        else
            _consecutiveUnchangedPolls++;
    }

    private bool IsStable() =>
        _totalPollCount >= MinPollsThreshold &&
        (_playlistVariants.Length >= EarlyStabilityVariantCount || _consecutiveUnchangedPolls >= MaxUnchangedPollsThreshold);
}