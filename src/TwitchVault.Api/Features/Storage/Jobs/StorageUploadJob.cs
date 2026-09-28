using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Storage.Jobs;

public sealed class StorageUploadJob(
    IDataStore dataStore,
    IStreamStorageService storageService,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    IOptionsMonitor<VaultOptions> vaultOptions,
    ILogger<StorageUploadJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.StorageUpload).Enabled)
            return;

        var pendingStreams = await dataStore.QueryAsync(
            ctx => ctx.Streams.PendingUpload().OrderBy(s => s.StartedAt).Take(10).ToListAsync(context.CancellationToken));

        if (pendingStreams.Count == 0)
            return;

        logger.LogInformation("StorageUploadJob found {Count} stream(s) pending upload recovery.", pendingStreams.Count);

        foreach (var stream in pendingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await UploadStreamAsync(stream, context.CancellationToken);
        }
    }

    private async Task UploadStreamAsync(Streams.Stream stream, CancellationToken cancellationToken)
    {
        var streamTitle = stream.Chapters.FirstOrDefault()?.Title ?? stream.Id;
        var localDirectory = stream.Folder.AbsolutePath;

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Upload skipped for '{Title}': directory '{Dir}' not found.", streamTitle, localDirectory);
            return;
        }

        var remainingSegments = new DirectoryInfo(localDirectory)
            .EnumerateFiles()
            .Where(f => HlsSegmentNaming.IsSegmentFile(f.FullName))
            .OrderBy(f => HlsSegmentNaming.GetSegmentIndex(f.FullName))
            .Select(f => f.FullName)
            .ToList();

        if (remainingSegments.Count > 0)
        {
            await dataStore.ExecuteAsync(() =>
            {
                stream.SetStorageOperationStatus(StorageOperationStatus.Uploading);
                dataStore.Save(stream);
                return Task.CompletedTask;
            });

            await UploadRemainingSegmentsAsync(
                remainingSegments,
                stream,
                cancellationToken);
        }

        await storageService.FinalizeStorageAsync(stream, cancellationToken);
    }

    private async Task UploadRemainingSegmentsAsync(
        List<string> remainingSegments,
        Streams.Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            var batchSize = Math.Max(5, vaultOptions.CurrentValue.UploadBatchSize);
            foreach (var batch in remainingSegments.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var succeeded = await storageService.UploadBatchAsync(
                    batch, stream, cancellationToken);

                if (succeeded)
                    continue;

                await dataStore.ExecuteAsync(() =>
                {
                    stream.SetStorageOperationStatus(StorageOperationStatus.UploadFailed);
                    dataStore.Save(stream);
                    return Task.CompletedTask;
                });

                logger.LogError("Backup upload batch failed for '{StreamId}'. Aborting.",
                    stream.Id);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Backup upload for stream '{StreamId}' was cancelled.", stream.Id);
        }
    }
}