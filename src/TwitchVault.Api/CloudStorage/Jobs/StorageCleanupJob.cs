using Quartz;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageCleanupJob(
    IStreamRepository streamRepository,
    ICloudStorageService storageService,
    IWebHostEnvironment env,
    ILogger<StorageCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.StorageOperationStatus == StorageOperationStatus.Deleting)
            .ToList();

        foreach (var stream in pendingStreams)
        {
            await DeleteStreamAsync(stream, context.CancellationToken);
        }
    }

    private async Task DeleteStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        if (stream.StorageLocation == StorageLocation.Remote)
        {
            var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
            var extractionResult = ManifestSegmentExtractor.ExtractAllSegments(File.ReadAllText(playlistPath));
            var remoteUrls = extractionResult.Segments
                .Select(seg => seg.Url)
                .Append(extractionResult.InitSegmentUrl ?? string.Empty);

            var result = await storageService.DeleteBatchAsync(stream.StorageInstanceName!, remoteUrls.ToList(), cancellationToken);
            if (result.IsFailure)
            {
                logger.LogError("Failed to delete remote data for stream {StreamId} on instance '{InstanceId}': {Error}",
                    stream.TwitchStreamId, stream.StorageInstanceName, result.Error);
                return;
            }
        }

        // Delete local folder and playlist on disk
        var localDir = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        await IOUtils.DeleteDirectoryWithRetriesAsync(localDir);

        await streamRepository.DeleteAsync(stream.TwitchStreamId);
        logger.LogInformation("Stream {StreamId} fully wiped from local and cloud storage.", stream.TwitchStreamId);
    }
}