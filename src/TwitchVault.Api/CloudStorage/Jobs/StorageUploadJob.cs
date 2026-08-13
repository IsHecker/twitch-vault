using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage.Jobs;

[DisallowConcurrentExecution]
public sealed class StorageUploadJob(
    IStreamRepository streamRepository,
    CloudStorageService storageService,
    SettingsService settingsService,
    IWebHostEnvironment env,
    ILogger<StorageUploadJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.BackgroundJobs[JobOptions.StorageUpload].Enabled)
            return;

        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.Finished
                && (s.StorageLocation == StorageLocation.Local || s.StorageOperationStatus == StorageOperationStatus.UploadFailed))
            .OrderBy(s => s.StartedAt);

        foreach (var stream in pendingStreams)
        {
            await UploadStreamAsync(stream, context.CancellationToken);
        }
    }

    private async Task UploadStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        if (stream.StorageOperationStatus == StorageOperationStatus.Deleting)
        {
            logger.LogWarning("INVALID_STATE: Stream is being or has been deleted for '{StreamId}'", stream.TwitchStreamId);
            return;
        }

        var streamTitle = stream.Chapters.FirstOrDefault()?.Title ?? stream.TwitchStreamId;
        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Upload skipped for '{Title}': directory '{Dir}' not found.", streamTitle, localDirectory);
            return;
        }

        if (stream.StorageLocation == StorageLocation.Local)
        {
            stream.SetStorageOperationStatus(StorageOperationStatus.Uploading);
            await streamRepository.UpdateAsync(stream);
        }

        var dir = new DirectoryInfo(localDirectory);
        var pendingSegments = dir.EnumerateFiles()
            .Where(f => HlsSegmentNaming.IsSegmentFile(f.FullName))
            .OrderBy(f => HlsSegmentNaming.GetSegmentIndex(f.FullName))
            .Select(f => new LocalSegment(f.FullName, f.Length))
            .ToList();

        var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
        var rewrittenPlaylistPath = $"{stream.Folder.RelativePath}/new-{StreamFolder.PlaylistFile}";

        if (pendingSegments.Count == 0)
        {
            logger.LogInformation("Stream '{Title}' has no segment files to upload.", streamTitle);
            await streamRepository.UpdateAsync(stream);
            return;
        }

        logger.LogInformation("Starting upload for '{Title}'. Segments: {Count}.", streamTitle, pendingSegments.Count);

        var result = await storageService.UploadStreamAsync(
            stream,
            pendingSegments,
            cancellationToken);

        if (result.IsFailure)
        {
            stream.SetStorageOperationStatus(StorageOperationStatus.UploadFailed);
            await streamRepository.UpdateAsync(stream);
            logger.LogError("Failed to upload segment chunk for '{Title}': {Error}", streamTitle, result.Error);
            return;
        }

        var uploadResult = result.Value;

        if (string.IsNullOrWhiteSpace(stream.StorageInstanceId))
            stream.SetStorageInstance(uploadResult.StorageInstanceId);

        await HlsPlaylistRewriter.RewriteSegmentsAsync(
            playlistPath,
            rewrittenPlaylistPath,
            uploadResult.RemoteUrls,
            uploadResult.SegmentsSkipped,
            cancellationToken);

        stream.SetStorageOperationStatus(StorageOperationStatus.Uploaded);
        stream.SetStorageLocation(StorageLocation.Remote);
        await streamRepository.UpdateAsync(stream);

        DeleteLocalSegments(pendingSegments, uploadResult.RemoteUrls.Count);
        File.Move(rewrittenPlaylistPath, playlistPath, overwrite: true);

        logger.LogInformation("Stream '{StreamId}' successfully uploaded ({Bytes} bytes) to storage instance '{Instance}'.",
            stream.TwitchStreamId, stream.SizeBytes, uploadResult.StorageInstanceId);
    }

    private static void DeleteLocalSegments(List<LocalSegment> segments, int deleteCount)
    {
        for (int i = 0; i < deleteCount; i++)
        {
            File.Delete(segments[i].LocalPath);
        }
    }
}