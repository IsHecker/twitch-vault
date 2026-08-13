using TwitchVault.Api.CloudStorage.Discord;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public class CloudStorageService(
    StorageRouter router,
    StorageQuotaTracker quotaTracker,
    StorageProviderRegistry providerRegistry,
    ProgressTracker progressTracker,
    IWebHostEnvironment env,
    ILogger<CloudStorageService> logger)
{
    public async Task<Result<StreamUploadResult>> UploadStreamAsync(
        Domain.Stream stream,
        IEnumerable<LocalSegment> segments,
        CancellationToken cancellationToken = default)
    {
        var progressKey = $"Upload:{stream.TwitchStreamId}";
        var selection = GetUploadStorageProvider(stream);
        if (selection.IsFailure)
            return selection.Error;

        var provider = selection.Value;

        var remoteUrls = new List<string>();
        var lastUploadedIndex = progressTracker.GetLastUploadedSegmentIndex(progressKey);
        quotaTracker.Allocate(provider.Options.Name, stream.SizeBytes);

        var segmentsToSkip = lastUploadedIndex;
        segments = segments.Skip(segmentsToSkip + 1);
        foreach (var segmentBatch in segments.Chunk(provider.Options.Behavior.MaxBatchSize))
        {
            if (cancellationToken.IsCancellationRequested)
                return Error.Failure("Upload operation cancelled.");

            var dataStreams = segmentBatch.Select(seg => new FileStream(
                seg.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81_920,
                useAsync: true));

            try
            {
                var uploadResult = await provider.UploadAsync(dataStreams, segmentBatch, cancellationToken);
                if (uploadResult.IsFailure)
                {
                    logger.LogError("Failed to upload this batch to provider '{Instance}': {Error}",
                        provider.Options.Name, uploadResult.Error);
                    return uploadResult.Error;
                }

                remoteUrls.AddRange(uploadResult.Value);
                lastUploadedIndex += segmentBatch.Length;
                await progressTracker.SaveProgressAsync(progressKey, lastUploadedIndex);
            }
            finally
            {
                foreach (var data in dataStreams)
                {
                    await data.DisposeAsync();
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        await progressTracker.RemoveProgressAsync(progressKey);
        return new StreamUploadResult(provider.Options.Name, segmentsToSkip, remoteUrls);
    }

    public async Task<Result> DeleteStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stream.StorageInstanceId))
            return Result.Success;

        try
        {
            var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
            var provider = providerRegistry.GetInstance(stream.StorageInstanceId);

            var deleteResult = await provider.DeleteBatchAsync(stream, File.ReadAllText(playlistPath), cancellationToken);
            if (deleteResult.IsFailure)
                return deleteResult.Error;

            quotaTracker.Release(stream.StorageInstanceId, stream.SizeBytes);
            return Result.Success;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting stream data for stream '{StreamId}' on provider instance '{Instance}'.",
                stream.TwitchStreamId, stream.StorageInstanceId);
            return Error.Failure($"Failed to delete stream data: {ex.Message}");
        }
    }

    private Result<ICloudStorageProvider> GetUploadStorageProvider(Domain.Stream stream)
    {
        if (string.IsNullOrWhiteSpace(stream.StorageInstanceId))
            return router.SelectUploader(stream.SizeBytes);

        return new Result<ICloudStorageProvider>(providerRegistry.GetInstance(stream.StorageInstanceId));
    }
}