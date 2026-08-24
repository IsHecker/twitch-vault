using TwitchVault.Api.CloudStorage;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Recording;

// TODO: Refactor and improve implementation when it's working.
public interface IStreamStorageService
{
    const string RemoteUrlsFileName = "remoteUrls.txt";

    Task<bool> UploadBatchAsync(
        IEnumerable<string> localFilePaths,
        Domain.Stream stream,
        StreamWriter remoteUrlsWriter,
        CancellationToken cancellationToken = default);

    Task<bool> TryFinalizeStorageAsync(
        Domain.Stream stream,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteStreamAsync(
        Domain.Stream stream,
        CancellationToken cancellationToken = default);
}

public sealed class StreamStorageService(
    ICloudStorageService cloudStorageService,
    IStreamRepository streamRepository,
    IWebHostEnvironment env,
    ILogger<StreamStorageService> logger) : IStreamStorageService
{
    // TODO: Why a streamwriter is being passed?
    public async Task<bool> UploadBatchAsync(
        IEnumerable<string> localFilePaths,
        Domain.Stream stream,
        StreamWriter remoteUrlsWriter,
        CancellationToken cancellationToken = default)
    {
        _ = localFilePaths.TryGetNonEnumeratedCount(out var filePathsCount);
        var storageFiles = new List<StorageFile>(filePathsCount);
        var localPathByFileName = new Dictionary<string, string>(filePathsCount, StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var path in localFilePaths)
            {
                var fs = File.OpenRead(path);
                var storageFile = new StorageFile(path, ResolveContentType(path), fs);
                storageFiles.Add(storageFile);
                localPathByFileName[storageFile.FileName] = path;
            }

            var uploadResult = await cloudStorageService.UploadAsync(
                storageFiles,
                stream.StorageInstanceName,
                cancellationToken);

            if (uploadResult.IsFailure)
            {
                logger.LogError("Batch upload failed for stream '{StreamId}': {Error}",
                    stream.TwitchStreamId, uploadResult.Error);
                return false;
            }

            var response = uploadResult.Value;
            if (string.IsNullOrWhiteSpace(stream.StorageInstanceName))
            {
                stream.SetStorageInstance(response.InstanceName);
                await streamRepository.UpdateAsync(stream);
            }

            foreach (var remoteUrl in response.RemoteUrls)
            {
                await remoteUrlsWriter.WriteLineAsync(remoteUrl.Url);
                if (localPathByFileName.TryGetValue(remoteUrl.FileName, out var localPath))
                    File.Delete(localPath);
            }

            await remoteUrlsWriter.FlushAsync(cancellationToken);
            return true;
        }
        finally
        {
            foreach (var file in storageFiles)
                await file.Content.DisposeAsync();
        }
    }

    public async Task<bool> TryFinalizeStorageAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var localDir = stream.Folder.GetAbsolutePath(env.ContentRootPath);
        if (!Directory.Exists(localDir))
        {
            logger.LogWarning("Cannot finalize storage for stream '{StreamId}': directory '{Dir}' not found.",
                stream.TwitchStreamId, localDir);
            return false;
        }

        var remainingSegments = new DirectoryInfo(localDir)
            .EnumerateFiles()
            .Where(f => HlsSegmentNaming.IsSegmentFile(f.FullName))
            .OrderBy(f => HlsSegmentNaming.GetSegmentIndex(f.FullName))
            .ToList();

        if (remainingSegments.Count > 0)
        {
            logger.LogInformation(
                "Storage finalization deferred for stream '{StreamId}': {Count} segment(s) still on disk. Backup uploader will process them.",
                stream.TwitchStreamId, remainingSegments.Count);
            return false;
        }

        var remoteUrlsFilePath = Path.Combine(localDir, IStreamStorageService.RemoteUrlsFileName);
        if (!File.Exists(remoteUrlsFilePath))
        {
            logger.LogWarning(
                "Cannot finalize storage for stream '{StreamId}': '{FileName}' not found.",
                stream.TwitchStreamId, IStreamStorageService.RemoteUrlsFileName);
            return false;
        }

        var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
        if (!File.Exists(playlistPath))
        {
            logger.LogWarning(
                "Cannot finalize storage for stream '{StreamId}': playlist '{Path}' not found.",
                stream.TwitchStreamId, playlistPath);
            return false;
        }

        var tempPlaylistPath = $"{playlistPath}.tmp";
        await HlsPlaylistRewriter.RewriteSegmentsAsync(playlistPath, tempPlaylistPath, remoteUrlsFilePath, cancellationToken);
        File.Move(tempPlaylistPath, playlistPath, overwrite: true);

        stream.SetStorageOperationStatus(StorageOperationStatus.Uploaded);
        stream.SetStorageLocation(StorageLocation.Remote);
        await streamRepository.UpdateAsync(stream);

        logger.LogInformation(
            "Stream '{StreamId}' storage finalized on instance '{Instance}'.",
            stream.TwitchStreamId, stream.StorageInstanceName);

        return true;
    }

    public async Task<bool> DeleteStreamAsync(
        Domain.Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (stream.StorageLocation == StorageLocation.Remote)
        {
            var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
            if (File.Exists(playlistPath))
            {
                // TODO: use the urls from the remoteurls file!
                var remoteUrls = PlaylistSegmentExtractor
                    .EnumerateSegments(File.ReadLines(playlistPath))
                    .Select(seg => seg.Url);

                var result = await cloudStorageService.DeleteBatchAsync(
                    stream.StorageInstanceName!, remoteUrls, cancellationToken);

                if (result.IsFailure)
                {
                    logger.LogError(
                        "Failed to delete remote segments for stream '{StreamId}' on instance '{Instance}': {Error}",
                        stream.TwitchStreamId, stream.StorageInstanceName, result.Error);
                    return false;
                }
            }
        }

        await streamRepository.DeleteAsync(stream.TwitchStreamId);
        var localDir = stream.Folder.GetAbsolutePath(env.ContentRootPath);
        await IOUtils.DeleteDirectoryWithRetriesAsync(localDir);
        return true;
    }

    public static string ResolveContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
}