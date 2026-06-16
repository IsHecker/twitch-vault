using TwitchVault.Api.Common;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public sealed class ThumbnailManager(
    Domain.Stream stream,
    IDateTimeProvider dateTimeProvider,
    ITwitchGqlClient twitchGqlClient,
    ILogger<ThumbnailManager> logger)
{
    private static readonly TimeSpan LiveSnapshotCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveSnapshotWindow = TimeSpan.FromMinutes(30);
    private readonly DateTime _sessionStartTime = dateTimeProvider.DateTimeNow;
    private DateTime _lastSnapshotTime = DateTime.MinValue;
    public string LocalThumbnailPath => Path.Combine(stream.FolderPath, "thumbnail.jpg");

    public async Task TryCaptureSnapshotAsync()
    {
        if (!CanCaptureSnapshot())
            return;

        try
        {
            await SaveThumbnailAsync(stream.ThumbnailUrl);
            _lastSnapshotTime = dateTimeProvider.DateTimeNow;
            logger.LogDebug("Captured live thumbnail snapshot");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to capture live thumbnail snapshot");
        }
    }

    private bool CanCaptureSnapshot()
    {
        var now = dateTimeProvider.DateTimeNow;
        var sessionElapsed = now - _sessionStartTime;
        var timeSinceLastSnapshot = now - _lastSnapshotTime;

        return sessionElapsed < LiveSnapshotWindow
            && timeSinceLastSnapshot >= LiveSnapshotCooldown;
    }

    private async Task SaveThumbnailAsync(string imageUrl)
    {
        using var imageStream = await twitchGqlClient.DownloadAsStreamAsync(imageUrl, CancellationToken.None);

        await using var fileStream = File.Create(LocalThumbnailPath);
        await imageStream.CopyToAsync(fileStream, CancellationToken.None);
    }
}