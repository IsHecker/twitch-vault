using System.Collections.Concurrent;
using CloudStorage.Core;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Storage;

namespace TwitchVault.Api.Recording;

// TODO: Refactor and improve implementation
public interface IStreamStorageService
{
    Task<bool> UploadBatchAsync(
        string[] localFilePaths,
        Domain.Stream stream,
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeStorageAsync(
        Domain.Stream stream,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteStreamAsync(
        Domain.Stream stream,
        CancellationToken cancellationToken = default);
}

public sealed class StreamStorageService(
    ICloudStorageService cloudStorageService,
    IDataStore dataStore,
    IWebHostEnvironment env,
    ILogger<StreamStorageService> logger) : IStreamStorageService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _streamLocks = new();

    private static readonly FileStreamOptions FileReadOptions = new()
    {
        Mode = FileMode.Open,
        Access = FileAccess.Read,
        Share = FileShare.Read,
        BufferSize = 4096,
        Options = FileOptions.Asynchronous | FileOptions.SequentialScan
    };

    public async Task<bool> UploadBatchAsync(
        string[] localFilePaths,
        Domain.Stream stream,
        CancellationToken cancellationToken = default)
    {
        var (storageFiles, localPaths) = OpenSourceFiles(localFilePaths);
        var uploadResult = await cloudStorageService.UploadAsync(storageFiles, stream.StorageInstanceName, cancellationToken);
        if (uploadResult.IsFailure)
        {
            logger.LogError("Batch upload failed for stream '{StreamId}': {Error}",
                stream.Id, uploadResult.Error);
            return false;
        }

        var response = uploadResult.Value;
        if (string.IsNullOrWhiteSpace(stream.StorageInstanceName))
        {
            await dataStore.ExecuteAsync(() =>
            {
                stream.SetStorageOperationStatus(StorageOperationStatus.Uploading);
                stream.SetStorageInstance(response.InstanceName);
                dataStore.Save(stream);
                return Task.CompletedTask;
            });
        }

        var streamLock = GetStreamLock(stream.Id);
        await streamLock.WaitAsync(cancellationToken);
        try
        {
            var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);
            var remoteUrlsFilePath = Path.Combine(localDirectory, StreamFolder.RemoteUrlsFile);
            await using var remoteUrlsWriter = new StreamWriter(
                new FileStream(remoteUrlsFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite));

            foreach (var remoteUrl in response.RemoteUrls)
            {
                await remoteUrlsWriter.WriteLineAsync($"{remoteUrl.FileName}\t{remoteUrl.Url}");
            }
            await remoteUrlsWriter.FlushAsync(cancellationToken);

            foreach (var remoteUrl in response.RemoteUrls)
            {
                var localPath = FindLocalPath(localPaths, remoteUrl.FileName);
                // if (localPath is null)
                //     continue;

                try
                {
                    File.Delete(localPath!);
                }
                catch (IOException ex)
                {
                    logger.LogWarning(ex,
                        "Uploaded '{FileName}' for stream '{StreamId}' but failed to delete local copy '{Path}'.",
                        remoteUrl.FileName, stream.Id, localPath);
                }
            }

            return true;
        }
        finally
        {
            streamLock.Release();
            foreach (var file in storageFiles)
            {
                await file.Content.DisposeAsync();
            }
        }
    }

    public async Task<bool> FinalizeStorageAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);
        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning("Cannot finalize storage for stream '{StreamId}': directory '{Dir}' not found.",
                stream.Id, localDirectory);
            return false;
        }

        var remainingSegments = new DirectoryInfo(localDirectory)
            .EnumerateFiles()
            .Where(f => HlsSegmentNaming.IsSegmentFile(f.FullName))
            .OrderBy(f => HlsSegmentNaming.GetSegmentIndex(f.FullName))
            .ToList();

        if (remainingSegments.Count > 0)
        {
            logger.LogInformation(
                "Storage finalization deferred for stream '{StreamId}': {Count} segment(s) still on disk. Backup uploader will process them.",
                stream.Id, remainingSegments.Count);
            return false;
        }

        var remoteUrlsFilePath = Path.Combine(localDirectory, StreamFolder.RemoteUrlsFile);
        if (!File.Exists(remoteUrlsFilePath))
        {
            logger.LogWarning(
                "Cannot finalize storage for stream '{StreamId}': '{FileName}' not found.",
                stream.Id, StreamFolder.RemoteUrlsFile);
            return false;
        }

        var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
        if (!File.Exists(playlistPath))
        {
            logger.LogWarning(
                "Cannot finalize storage for stream '{StreamId}': playlist '{Path}' not found.",
                stream.Id, playlistPath);
            return false;
        }

        var tempPlaylistPath = $"{playlistPath}.tmp";
        await HlsPlaylistRewriter.RewriteSegmentsAsync(playlistPath, tempPlaylistPath, remoteUrlsFilePath, cancellationToken);
        File.Move(tempPlaylistPath, playlistPath, overwrite: true);

        await dataStore.ExecuteAsync(() =>
        {
            stream.SetStorageOperationStatus(StorageOperationStatus.Uploaded);
            stream.SetStorageLocation(StorageLocation.Remote);
            dataStore.Save(stream);
            return Task.CompletedTask;
        });

        if (_streamLocks.TryRemove(stream.Id, out var sem))
            sem.Dispose();

        return true;
    }

    public async Task<bool> DeleteStreamAsync(
        Domain.Stream stream,
        CancellationToken cancellationToken = default)
    {
        var urlFilePath = stream.Folder.GetAbsolutePath(env.ContentRootPath);
        if (stream.StorageLocation == StorageLocation.Remote && File.Exists(urlFilePath))
        {
            var remoteUrls = File.ReadLines(urlFilePath);

            logger.LogInformation("Deleting stream '{StreamId}' (Instance: '{Instance}')...",
                stream.Id, stream.StorageInstanceName);

            var result = await cloudStorageService.DeleteBatchAsync(
                stream.StorageInstanceName!, remoteUrls, cancellationToken);

            if (result.IsFailure)
            {
                logger.LogError(
                    "Failed to delete remote segments for stream '{StreamId}' on instance '{Instance}': {Error}",
                    stream.Id, stream.StorageInstanceName, result.Error);
                return false;
            }
        }

        await dataStore.ExecuteAsync(async () => await dataStore.DeleteAsync<Domain.Stream, string>(stream.Id));

        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);
        await IOUtils.DeleteDirectoryWithRetriesAsync(localDirectory);

        if (_streamLocks.TryRemove(stream.Id, out var sem))
            sem.Dispose();

        return true;
    }

    private static (StorageFile[] Files, string[] Paths) OpenSourceFiles(string[] localFilePaths)
    {
        var storageFiles = new StorageFile[localFilePaths.Length];
        for (var i = 0; i < localFilePaths.Length; i++)
        {
            var path = localFilePaths[i];
            storageFiles[i] = new StorageFile(path, ResolveContentType(path), new FileStream(path, FileReadOptions));
        }

        return (storageFiles, localFilePaths);
    }

    private static string? FindLocalPath(string[] paths, string fileName)
    {
        for (var i = 0; i < paths.Length; i++)
        {
            if (Path.GetFileName(paths[i].AsSpan()).Equals(fileName, StringComparison.OrdinalIgnoreCase))
                return paths[i];
        }
        return null;
    }

    private SemaphoreSlim GetStreamLock(string streamId)
    {
        if (_streamLocks.TryGetValue(streamId, out var existing))
            return existing;

        return _streamLocks.GetOrAdd(streamId, new SemaphoreSlim(1, 1));
    }

    private static string ResolveContentType(string filePath)
    {
        ReadOnlySpan<char> extension = Path.GetExtension(filePath.AsSpan());

        Span<char> lower = stackalloc char[extension.Length];
        extension.ToLowerInvariant(lower);

        return lower switch
        {
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}