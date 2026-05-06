using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public sealed class ThumbnailManager(
    Models.Stream stream,
    StreamSegment streamSegment,
    StreamRepository streamRepository,
    TwitchClient twitchClient,
    ILogger<ThumbnailManager> logger)
{
    private static readonly TimeSpan LiveSnapshotCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveSnapshotWindow = TimeSpan.FromMinutes(30);

    private readonly DateTime _sessionStartTime = DateTime.Now;
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    public string LocalThumbnailPath => Path.Combine(streamSegment.FolderPath, "thumbnail.jpg");

    public async Task TryCaptureSnapshotAsync(string channelName)
    {
        if (!CanCaptureSnapshot())
            return;

        try
        {
            if (string.IsNullOrEmpty(stream.TwitchVodId))
            {
                await DiscoverVodIdAsync(channelName);
            }

            await SaveThumbnailAsync(streamSegment.ThumbnailUrl);
            _lastSnapshotTime = DateTime.Now;

            logger.LogInformation(
                "Captured live thumbnail snapshot for segment {SegmentNumber}",
                streamSegment.SegmentNumber);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to capture live thumbnail snapshot for segment {SegmentNumber}",
                streamSegment.SegmentNumber);
        }
    }

    public async Task TrySaveVodThumbnailAsync(string channelName)
    {
        try
        {
            if (string.IsNullOrEmpty(stream.TwitchVodId))
            {
                await DiscoverVodIdAsync(channelName);
            }

            if (string.IsNullOrEmpty(stream.TwitchVodId))
                return;

            var thumbnailUrl = await twitchClient.GetVODThumbnailUrlAsync(stream.TwitchVodId, CancellationToken.None);
            if (string.IsNullOrEmpty(thumbnailUrl))
                return;

            await SaveThumbnailAsync(thumbnailUrl);

            logger.LogInformation(
                "Updated thumbnail for segment {SegmentNumber} to VOD thumbnail {VodId}",
                streamSegment.SegmentNumber, stream.TwitchVodId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to update VOD thumbnail for segment {SegmentNumber}",
                streamSegment.SegmentNumber);
        }
    }

    private async Task DiscoverVodIdAsync(string channelName)
    {
        try
        {
            var (streamId, vodId) = await twitchClient.GetStreamVODIdAsync(channelName, CancellationToken.None);
            if (streamId != stream.TwitchStreamId || string.IsNullOrEmpty(vodId))
                return;

            stream.TwitchVodId = vodId;
            await streamRepository.UpdateStreamAsync(stream);
            logger.LogInformation("Thumbnail: Linked to VOD {VodId}.", vodId);
        }
        catch { }
    }

    private bool CanCaptureSnapshot()
    {
        var now = DateTime.Now;
        var sessionElapsed = now - _sessionStartTime;
        var timeSinceLastSnapshot = now - _lastSnapshotTime;

        return sessionElapsed < LiveSnapshotWindow
            && timeSinceLastSnapshot >= LiveSnapshotCooldown;
    }

    private async Task SaveThumbnailAsync(string imageUrl)
    {
        using var imageStream = await twitchClient.DownloadAsStreamAsync(imageUrl, CancellationToken.None);
        await using var fileStream = File.Create(LocalThumbnailPath);
        await imageStream.CopyToAsync(fileStream, CancellationToken.None);
    }
}