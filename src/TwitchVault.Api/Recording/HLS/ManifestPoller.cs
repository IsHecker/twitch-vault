using TwitchVault.Api.Twitch;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public interface IManifestPoller
{
    Task<(string? Manifest, bool HasQualityChanged)> GetNextManifestAsync(
        string channelName,
        CancellationToken cancellationToken);
}

public sealed class ManifestPoller(
    ITwitchGqlClient twitchClient,
    IChannelRepository channelRepository,
    IDateTimeProvider dateTimeProvider,
    ILogger<ManifestPoller> logger) : IManifestPoller
{
    private const int MinPollsThreshold = 5;
    private const int MaxUnchangedPollsThreshold = 10;
    private const int EarlyStabilityVariantCount = 5;
    private static readonly TimeSpan MasterPlaylistRefreshInterval = TimeSpan.FromSeconds(30);

    private StreamVariant[] _variants = [];
    private int _consecutiveUnchangedPolls;
    private int _totalPollCount;
    private DateTime _lastRefreshedAt = DateTime.MinValue;

    private string? _activeVariantUrl;
    private int _activeQualityRank = -1;

    private bool IsRefreshNeeded =>
        dateTimeProvider.DateTimeNow - _lastRefreshedAt >= MasterPlaylistRefreshInterval;

    private bool IsStable =>
        _totalPollCount >= MinPollsThreshold &&
        (_variants.Length >= EarlyStabilityVariantCount || _consecutiveUnchangedPolls >= MaxUnchangedPollsThreshold);

    public async Task<(string? Manifest, bool HasQualityChanged)> GetNextManifestAsync(
        string channelName,
        CancellationToken cancellationToken)
    {
        if (!IsStable)
            await RefreshVariantsAsync(channelName, cancellationToken);

        if (_variants.Length == 0)
            return (null, false);

        var (rank, url) = await ResolveQualityAsync(channelName);

        var hasQualityChanged = rank != _activeQualityRank || url != _activeVariantUrl;
        (_activeQualityRank, _activeVariantUrl) = (rank, url);

        var manifest = await twitchClient.GetPlaylistContentAsync(url, cancellationToken);
        return (string.IsNullOrWhiteSpace(manifest) ? null : manifest, hasQualityChanged);
    }

    private async Task RefreshVariantsAsync(string channelName, CancellationToken cancellationToken)
    {
        if (!IsRefreshNeeded)
            return;

        var variants = await FetchVariantsAsync(channelName, cancellationToken);
        if (variants.Length == 0)
            return;

        _variants = variants;
        _lastRefreshedAt = dateTimeProvider.DateTimeNow;

        RecordPoll(variants.Length);
        if (!IsStable)
            return;

        logger.LogDebug(
            "Master playlist stabilized with {Count} quality variants. (Source: {Bandwidth} bps)",
            _variants.Length, _variants[^1].Bandwidth);
    }

    private async Task<(int Rank, string Url)> ResolveQualityAsync(string channelName)
    {
        var channels = await channelRepository.GetAllAsync();
        var requestedRank = channels.First(c => c.Name == channelName).QualityRank - 1;
        var clampedRank = Math.Clamp(requestedRank, 0, _variants.Length - 1);
        if (requestedRank != clampedRank)
        {
            logger.LogInformation(
                "Quality rank {Requested} unavailable; clamped to {Clamped} of {Total}.",
                requestedRank + 1, clampedRank + 1, _variants.Length);
        }

        return (clampedRank, _variants[clampedRank].Url);
    }

    private async Task<StreamVariant[]> FetchVariantsAsync(string channelName, CancellationToken cancellationToken)
    {
        var masterPlaylist = await twitchClient.GetMasterPlaylistAsync(channelName, cancellationToken);
        if (string.IsNullOrWhiteSpace(masterPlaylist))
            return [];

        return StreamVariantExtractor.ExtractVariants(masterPlaylist);
    }

    private void RecordPoll(int variantCount)
    {
        if (variantCount > _variants.Length)
            _consecutiveUnchangedPolls = 0;
        else
            _consecutiveUnchangedPolls++;

        _totalPollCount++;
    }
}