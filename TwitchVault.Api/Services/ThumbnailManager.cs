using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Services;

public sealed class ThumbnailManager(
    Models.Stream stream,
    TwitchClient twitchClient,
    ILogger<ThumbnailManager> logger)
{
    private static readonly TimeSpan LiveSnapshotCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveSnapshotWindow = TimeSpan.FromMinutes(30);

    private readonly DateTime _sessionStartTime = DateTime.Now;
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    public string LocalThumbnailPath => Path.Combine(stream.FolderPath, "thumbnail.jpg");

    public async Task TryCaptureSnapshotAsync()
    {
        if (!CanCaptureSnapshot())
            return;

        try
        {
            await SaveThumbnailAsync(stream.ThumbnailUrl);
            _lastSnapshotTime = DateTime.Now;

            logger.LogInformation(
                "Captured live thumbnail snapshot for stream {StreamId}",
                stream.TwitchStreamId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to capture live thumbnail snapshot for stream {StreamId}",
                stream.TwitchStreamId);
        }
    }

    public async Task TrySaveVodThumbnailAsync(string channelName)
    {
        try
        {
            var thumbnailUrl = await twitchClient.GetVODThumbnailUrlAsync(channelName, CancellationToken.None);
            if (string.IsNullOrEmpty(thumbnailUrl))
                return;

            await SaveThumbnailAsync(thumbnailUrl);

            logger.LogInformation(
                "Updated thumbnail for stream {StreamId} to VOD thumbnail", stream.TwitchStreamId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch/download VOD thumbnail for stream {StreamId}. Falling back to snapshot.",
                stream.TwitchStreamId);
        }
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