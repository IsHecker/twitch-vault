using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage.Jobs;

public sealed class StorageUploadJob(
    IStreamRepository streamRepository,
    IStreamStorageService storageService,
    StreamJobCoordinator jobCoordinator,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    IWebHostEnvironment env,
    ILogger<StorageUploadJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.StorageUpload).Enabled)
            return;

        var stream = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.Finished
                && (s.StorageLocation == StorageLocation.Local || s.StorageOperationStatus == StorageOperationStatus.UploadFailed))
            .OrderBy(s => s.StartedAt)
            .FirstOrDefault();

        if (stream is null)
            return;

        await UploadStreamAsync(stream, context.CancellationToken);
    }

    private async Task UploadStreamAsync(Domain.Stream stream, CancellationToken jobCancellationToken)
    {
        if (stream.StorageOperationStatus == StorageOperationStatus.Deleting)
        {
            logger.LogWarning("INVALID_STATE: Stream is being or has been deleted for '{StreamId}'", stream.TwitchStreamId);
            return;
        }

        var uploadToken = jobCoordinator.RegisterUpload(stream.TwitchStreamId);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(jobCancellationToken, uploadToken);
        var cancellationToken = linkedCts.Token;

        var streamTitle = stream.Chapters.FirstOrDefault()?.Title ?? stream.TwitchStreamId;
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

        var remoteUrlsFilePath = Path.Combine(localDirectory, IStreamStorageService.RemoteUrlsFileName);

        if (remainingSegments.Count > 0)
        {
            logger.LogInformation(
                "Backup upload for '{Title}': {Count} segment(s) left on disk.",
                streamTitle, remainingSegments.Count);

            stream.SetStorageOperationStatus(StorageOperationStatus.Uploading);
            await streamRepository.UpdateAsync(stream);

            await UploadRemainingSegmentsAsync(
                remainingSegments, stream, remoteUrlsFilePath, cancellationToken);
        }
        else
        {
            logger.LogInformation(
                "Backup upload for '{Title}': all segments already uploaded by live batcher.",
                streamTitle);
        }

        await storageService.TryFinalizeStorageAsync(stream, cancellationToken);

        jobCoordinator.CompleteUpload(stream.TwitchStreamId); // ALWAYS unblocks a waiting delete
    }

    private async Task UploadRemainingSegmentsAsync(
        List<string> remainingSegments,
        Domain.Stream stream,
        string remoteUrlsFilePath,
        CancellationToken cancellationToken)
    {
        await using var remoteUrlsWriter = new StreamWriter(remoteUrlsFilePath, append: true);

        try
        {
            const int BatchSize = 5;
            foreach (var batch in remainingSegments.Chunk(BatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var succeeded = await storageService.UploadBatchAsync(
                    batch, stream, remoteUrlsWriter, cancellationToken);

                if (!succeeded)
                {
                    stream.SetStorageOperationStatus(StorageOperationStatus.UploadFailed);
                    await streamRepository.UpdateAsync(stream);
                    logger.LogError("Backup upload batch failed for '{StreamId}'. Aborting.",
                        stream.TwitchStreamId);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Backup upload for stream '{StreamId}' was cancelled.", stream.TwitchStreamId);
        }
    }
}