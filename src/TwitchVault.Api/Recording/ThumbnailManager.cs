using TwitchVault.Api.Common;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IThumbnailManager
{
    Task TryCaptureSnapshotAsync(string channelName, Domain.Stream stream);
}

public sealed class ThumbnailManager(
    ITwitchGqlClient twitchGqlClient,
    IDateTimeProvider dateTimeProvider,
    IStorageService fileSystem,
    ILogger<ThumbnailManager> logger) : IThumbnailManager
{
    private static readonly TimeSpan LiveSnapshotCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LiveSnapshotWindow = TimeSpan.FromMinutes(30);
    private readonly DateTime _sessionStartTime = dateTimeProvider.DateTimeNow;
    private DateTime _lastSnapshotTime = DateTime.MinValue;

    public async Task TryCaptureSnapshotAsync(string channelName, Domain.Stream stream)
    {
        if (!CanCaptureSnapshot())
            return;

        try
        {
            await SaveThumbnailAsync(BuildLiveThumbnailUrl(channelName), stream.Folder.ThumbnailPath);
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

        await using var fileStream = fileSystem.OpenWrite(savePath, FileMode.Create);
        await imageStream.CopyToAsync(fileStream, CancellationToken.None);
    }

    private static string BuildLiveThumbnailUrl(string channelName) =>
        $"https://static-cdn.jtvnw.net/previews-ttv/live_user_{channelName}-1280x720.jpg";
}