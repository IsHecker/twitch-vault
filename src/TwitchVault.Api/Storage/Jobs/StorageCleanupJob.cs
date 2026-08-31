using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Storage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageCleanupJob(
    IDataStore dataStore,
    IStreamStorageService storageService,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    ILogger<StorageCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.StorageCleanup).Enabled)
            return;

        var pendingStreams = await dataStore.QueryAsync(
            dbContext => dbContext.Streams.PendingDeletion().ToListAsync(context.CancellationToken));

        foreach (var stream in pendingStreams)
        {
            var succeeded = await storageService.DeleteStreamAsync(stream, context.CancellationToken);
            if (!succeeded)
                return;

            logger.LogInformation("Stream '{StreamId}' fully wiped from local and cloud storage.", stream.Id);

            await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
        }
    }
}