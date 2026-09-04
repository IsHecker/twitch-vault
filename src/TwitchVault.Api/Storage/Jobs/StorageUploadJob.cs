using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Storage.Jobs;

public sealed class StorageUploadJob(
    IDataStore dataStore,
    IStreamStorageService storageService,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    IWebHostEnvironment env,
    ILogger<StorageUploadJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.StorageUpload).Enabled)
            return;

        var stream = await dataStore.QueryAsync<Domain.Stream, Domain.Stream?>(
            streams => streams.PendingUpload().OrderBy(s => s.StartedAt).FirstOrDefaultAsync(context.CancellationToken));

        if (stream is null)
            return;

        await UploadStreamAsync(stream, context.CancellationToken);
    }

    private async Task UploadStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var streamTitle = stream.Chapters.FirstOrDefault()?.Title ?? stream.Id;
        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Upload skipped for '{Title}': directory '{Dir}' not found.", streamTitle, localDirectory);
            return;
        }

        // Segments still on disk are segments the live uploader failed to upload (or never ran).
        var remainingSegments = new DirectoryInfo(localDirectory)
            .EnumerateFiles()
            .Where(f => HlsSegmentNaming.IsSegmentFile(f.FullName))
            .OrderBy(f => HlsSegmentNaming.GetSegmentIndex(f.FullName))
            .Select(f => f.FullName)
            .ToList();

        if (remainingSegments.Count > 0)
        {
            logger.LogInformation(
                "Backup upload for '{Title}': {Count} segment(s) left on disk.",
                streamTitle, remainingSegments.Count);

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
        else
        {
            logger.LogInformation(
                "Backup upload for '{Title}': all segments already uploaded by live batcher.",
                streamTitle);
        }

        await storageService.FinalizeStorageAsync(stream, cancellationToken);
    }

    private async Task UploadRemainingSegmentsAsync(
        List<string> remainingSegments,
        Domain.Stream stream,
        CancellationToken cancellationToken)
    {
        try
        {
            const int BatchSize = 5;
            foreach (var batch in remainingSegments.Chunk(BatchSize))
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