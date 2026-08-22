using Quartz;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.CloudStorage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageCleanupJob(
    IStreamRepository streamRepository,
    IStreamStorageService storageService,
    ILogger<StorageCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status != StreamStatus.Recording
                && (s.StorageOperationStatus == StorageOperationStatus.DeleteRequest
                || s.StorageOperationStatus == StorageOperationStatus.Deleting))
            .ToList();

        foreach (var stream in pendingStreams)
        {
            await DeleteStreamAsync(stream, context.CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }

    private async Task DeleteStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var succeeded = await storageService.DeleteStreamAsync(stream, cancellationToken);
        if (!succeeded)
            return;

        await streamRepository.DeleteAsync(stream.TwitchStreamId);
        logger.LogInformation("Stream '{StreamId}' fully wiped from local and cloud storage.", stream.TwitchStreamId);
    }
}