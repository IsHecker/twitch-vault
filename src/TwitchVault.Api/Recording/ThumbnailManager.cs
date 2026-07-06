using TwitchVault.Api.Common;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IThumbnailManager
{
    Task TryCaptureSnapshotAsync(Domain.Stream stream);
}

public sealed class ThumbnailManager(
    ITwitchGqlClient twitchGqlClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<ThumbnailManager> logger) : IThumbnailManager
{
    private static readonly TimeSpan LiveSnapshotCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveSnapshotWindow = TimeSpan.FromMinutes(30);
    private readonly DateTime _sessionStartTime = dateTimeProvider.DateTimeNow;
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    public async Task TryCaptureSnapshotAsync(Domain.Stream stream)
    {
        if (!CanCaptureSnapshot())
            return;

        try
        {
            await SaveThumbnailAsync(stream.ThumbnailUrl, stream.Folder.ThumbnailPath);
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

    private async Task SaveThumbnailAsync(string imageUrl, string savePath)
    {
        using var imageStream = await twitchGqlClient.DownloadAsStreamAsync(imageUrl, CancellationToken.None);

        await using var fileStream = File.Create(savePath);
        await imageStream.CopyToAsync(fileStream, CancellationToken.None);
    }
}