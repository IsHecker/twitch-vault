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

            logger.LogDebug("Captured live thumbnail snapshot");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to capture live thumbnail snapshot");
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