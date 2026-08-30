using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Storage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageCleanupJob(
    IDbContextFactory<AppDbContext> contextFactory,
    IStreamStorageService storageService,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    ILogger<StorageCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.StorageCleanup).Enabled)
            return;

        await using var db = await contextFactory.CreateDbContextAsync(context.CancellationToken);
        var pendingStreams = await db.Streams
            .PendingDeletion()
            .ToListAsync(context.CancellationToken);

        foreach (var stream in pendingStreams)
        {
            await DeleteStreamAsync(stream, context.CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(5), context.CancellationToken);
        }
    }

    private async Task DeleteStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var succeeded = await storageService.DeleteStreamAsync(stream, cancellationToken);
        if (!succeeded)
            return;

        logger.LogInformation("Stream '{StreamId}' fully wiped from local and cloud storage.", stream.Id);
    }
}