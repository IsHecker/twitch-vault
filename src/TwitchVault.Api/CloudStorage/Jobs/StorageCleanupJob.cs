using Quartz;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.CloudStorage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageCleanupJob(
    IStreamRepository streamRepository,
    CloudStorageService storageService,
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
        if (!string.IsNullOrEmpty(stream.StorageInstanceId))
        {
            var result = await storageService.DeleteStreamAsync(stream, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogError("Failed to delete remote data for stream {StreamId} on instance '{InstanceId}': {Error}",
                    stream.TwitchStreamId, stream.StorageInstanceId, result.Error);
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