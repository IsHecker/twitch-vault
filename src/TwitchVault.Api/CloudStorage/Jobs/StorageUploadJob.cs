using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Discord;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageUploadJob(
    IStreamRepository streamRepository,
    IInstanceSelector instanceSelector,
    UploadProgressService progressService,
    UniversalPlaylistRewriter playlistRewriter,
    StreamLockRegistry lockRegistry,
    SettingsService settingsService,
    IWebHostEnvironment env,
    ILogger<StorageUploadJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.BackgroundJobs[JobOptions.DiscordUpload].Enabled)
            return;

        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.Finished
                && (s.Storage == StorageLocation.Local || s.Storage == StorageLocation.Uploading))
            .OrderBy(s => s.StartedAt);

        foreach (var stream in pendingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await UploadStreamSegmentsAsync(stream, context.CancellationToken);
        }
    }

    private async Task UploadStreamSegmentsAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        // Try acquiring exclusive lock on stream
        await using var streamLock = await lockRegistry.TryAcquireLockAsync(stream.TwitchStreamId);
        if (streamLock is null)
        {
            logger.LogDebug("Upload skipped for stream '{StreamId}': lock held by another process.", stream.TwitchStreamId);
            return;
        }

        var streamTitle = stream.Chapters.FirstOrDefault()?.Title ?? stream.TwitchStreamId;
        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Upload skipped for '{Title}': directory '{Dir}' not found.", streamTitle, localDirectory);
            return;
        }

        var provider = await instanceSelector.SelectInstanceForStreamAsync(stream);

        var lastUploadedIndex = progressService.GetLastUploadedSegmentIndex(stream.TwitchStreamId);
        var pendingSegments = Directory.EnumerateFiles(localDirectory, "*")
            .Where(HlsSegmentNaming.IsSegmentFile)
            .OrderBy(HlsSegmentNaming.GetSegmentIndex)
            .Skip(lastUploadedIndex + 1)
            .ToList();

        var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);

        if (pendingSegments.Count == 0)
        {
            logger.LogInformation("Stream '{Title}' has no segment files to upload.", streamTitle);
            await streamRepository.UpdateAsync(stream);
            await progressService.RemoveProgressAsync(stream.TwitchStreamId);
            return;
        }

        if (stream.Storage == StorageLocation.Local)
        {
            stream.SetStorageLocation(StorageLocation.Uploading);
            await streamRepository.UpdateAsync(stream);
        }

        logger.LogInformation("Starting upload for '{Title}' using instance '{InstanceId}'. Remaining: {Count} segments.",
            streamTitle, provider.ProviderInstanceId, pendingSegments.Count);

        foreach (var batch in pendingSegments.Chunk(provider.Capabilities.MaxBatchSize))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            // Cooperative cancellation check before batch upload
            var currentStream = await streamRepository.GetByIdAsync(stream.TwitchStreamId);
            if (currentStream is null || currentStream.Status == StreamStatus.PendingDeletion || currentStream.Status == StreamStatus.Deleting)
            {
                logger.LogWarning("Upload aborted for '{Title}': stream marked for deletion.", streamTitle);
                return; // Exit and release stream lock so cleanup job can process deletion
            }

            var result = await provider.UploadBatchAsync(batch, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogError("Failed to upload segment chunk for '{Title}': {Error}", streamTitle, result.Error);
                return;
            }

            await playlistRewriter.RewritePlaylistSegmentsOnDiskAsync(playlistPath, result.Value.Segments, cancellationToken);

            lastUploadedIndex += batch.Length;
            await progressService.SaveProgressAsync(stream.TwitchStreamId, lastUploadedIndex);

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        stream.SetStorageLocation(StorageLocation.Both);
        await streamRepository.UpdateAsync(stream);

        DeleteLocalSegments(localDirectory);
        await progressService.RemoveProgressAsync(stream.TwitchStreamId);

        logger.LogInformation("Stream '{Title}' successfully uploaded to storage instance '{InstanceId}'.", streamTitle, provider.ProviderInstanceId);
    }

    private static void DeleteLocalSegments(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*").Where(HlsSegmentNaming.IsSegmentFile))
        {
            try { File.Delete(file); } catch { /* Ignore individual file locks */ }
        }
    }
}