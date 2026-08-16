using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage.Jobs;

public sealed class StorageUploadJob(
    IStreamRepository streamRepository,
    ICloudStorageService storageService,
    StreamJobCoordinator jobCoordinator,
    SettingsService settingsService,
    IWebHostEnvironment env,
    ILogger<StorageUploadJob> logger) : IJob
{
    private const int BatchSize = 1;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.BackgroundJobs[JobOptions.StorageUpload].Enabled)
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

        if (stream.StorageLocation == StorageLocation.Local)
        {
            stream.SetStorageOperationStatus(StorageOperationStatus.Uploading);
            await streamRepository.UpdateAsync(stream);
        }

        var dir = new DirectoryInfo(localDirectory);
        var pendingSegments = dir.EnumerateFiles()
            .Where(f => HlsSegmentNaming.IsSegmentFile(f.FullName))
            .OrderBy(f => HlsSegmentNaming.GetSegmentIndex(f.FullName))
            .ToList();

        var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
        var tempPlaylistPath = $"{playlistPath}.tmp";

        if (pendingSegments.Count == 0)
        {
            logger.LogInformation("Stream '{Title}' has no segment files to upload.", streamTitle);
            await streamRepository.UpdateAsync(stream);
            return;
        }

        logger.LogInformation("Starting upload for '{Title}'. Segments: {Count}.", streamTitle, pendingSegments.Count);

        var allRemoteUrls = new List<RemoteUrl>();
        try
        {
            foreach (var segmentBatch in pendingSegments.Chunk(BatchSize))
            {
                var storageFiles = segmentBatch.Select(seg =>
                {
                    var segmentPath = seg.FullName;
                    var content = new FileStream(
                        segmentPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 81_920,
                        useAsync: true);

                    return new StorageFile(segmentPath, ResolveContentType(segmentPath), content);
                });

                try
                {
                    var uploadResult = await storageService.UploadAsync(
                        storageFiles.ToList(),
                        stream.StorageInstanceName,
                        cancellationToken);

                    if (uploadResult.IsFailure)
                    {
                        stream.SetStorageOperationStatus(StorageOperationStatus.UploadFailed);
                        await streamRepository.UpdateAsync(stream);
                        logger.LogError("Failed to upload this batch to provider: {Error}",
                            uploadResult.Error);
                        return;
                    }

                    var (instanceName, remoteUrls) = uploadResult.Value;

                    allRemoteUrls.AddRange(remoteUrls);

                    if (string.IsNullOrWhiteSpace(stream.StorageInstanceName))
                    {
                        stream.SetStorageInstance(uploadResult.Value.InstanceName);
                        await streamRepository.UpdateAsync(stream);
                    }
                }
                catch (OperationCanceledException) { }
                finally
                {
                    foreach (var file in storageFiles)
                    {
                        await file.Content.DisposeAsync();
                    }
                }

                // await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(2, 5)), cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Upload for stream '{StreamId}' was cancelled.", stream.TwitchStreamId);
        }

        await HlsPlaylistRewriter.RewriteSegmentsAsync(
            playlistPath,
            tempPlaylistPath,
            allRemoteUrls.ToDictionary(key => key.LocalFilePath, val => val.Url),
            jobCancellationToken);

        stream.SetStorageOperationStatus(StorageOperationStatus.Uploaded);
        stream.SetStorageLocation(StorageLocation.Remote);
        await streamRepository.UpdateAsync(stream);

        // DeleteLocalSegments(allRemoteUrls);
        File.Move(tempPlaylistPath, playlistPath, overwrite: true);

        logger.LogInformation("Stream '{StreamId}' successfully uploaded ({Bytes} bytes) to storage instance '{Instance}'.",
            stream.TwitchStreamId, stream.SizeBytes, stream.StorageInstanceName);

        jobCoordinator.CompleteUpload(stream.TwitchStreamId); // ALWAYS unblocks a waiting delete
    }

    private static void DeleteLocalSegments(List<RemoteUrl> remoteUrls)
    {
        foreach (var remoteUrl in remoteUrls)
        {
            File.Delete(remoteUrl.LocalFilePath);
        }
    }

    private static string ResolveContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
}