using Quartz;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.CloudStorage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageCleanupJob(
    IStreamRepository streamRepository,
    StorageProviderRegistry providerFactory,
    StreamLockRegistry lockRegistry,
    IWebHostEnvironment env,
    ILogger<StorageCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.PendingDeletion || s.Status == StreamStatus.Deleting);

        foreach (var stream in pendingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            // Acquire exclusive lock on stream. If locked (e.g. upload job active), skip stream on this tick.
            await using var streamLock = await lockRegistry.TryAcquireLockAsync(stream.TwitchStreamId);
            if (streamLock is null)
            {
                logger.LogInformation("Cleanup skipped for stream {StreamId} on this tick: lock held by active upload process.", stream.TwitchStreamId);
                continue;
            }

            if (stream.Status == StreamStatus.PendingDeletion)
            {
                stream.SetStatus(StreamStatus.Deleting);
                await streamRepository.UpdateAsync(stream);
            }

            await DeleteStreamDataInternalAsync(stream, context.CancellationToken);
        }
    }

    private async Task DeleteStreamDataInternalAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(stream.StorageInstanceId))
        {
            try
            {
                var provider = providerFactory.GetInstance(stream.StorageInstanceId);
                var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);

                var result = await provider.DeleteStreamDataAsync(playlistPath, null!, cancellationToken);
                if (result.IsFailure)
                {
                    logger.LogError("Failed to delete remote data for stream {StreamId} on instance '{InstanceId}': {Error}",
                        stream.TwitchStreamId, stream.StorageInstanceId, result.Error);
                    return; // Retries on next job run
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unexpected error deleting remote data for stream {StreamId}.", stream.TwitchStreamId);
                return;
            }
        }

        // Delete local folder and playlist on disk
        var localDir = stream.Folder.GetAbsolutePath(env.ContentRootPath);
        if (Directory.Exists(localDir))
        {
            try
            {
                await IOUtils.DeleteDirectoryWithRetriesAsync(localDir);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete local folder '{Dir}' for stream {StreamId}.", localDir, stream.TwitchStreamId);
            }
        }

        await streamRepository.DeleteAsync(stream.TwitchStreamId);
        logger.LogInformation("Stream {StreamId} fully wiped from storage and database.", stream.TwitchStreamId);
    }
}