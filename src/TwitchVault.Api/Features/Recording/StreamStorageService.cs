using System.Collections.Concurrent;
using PolyStore;

namespace TwitchVault.Api.Features.Recording;

public interface IStreamStorageService
{
    Task<bool> UploadBatchAsync(
        string[] localFilePaths,
        Streams.Stream stream,
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeStorageAsync(
        Streams.Stream stream,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteStreamAsync(
        Streams.Stream stream,
        CancellationToken cancellationToken = default);
}

public sealed class StreamStorageService(
    IPolyStore polyStore,
    IDataStore dataStore,
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
        Streams.Stream stream,
        CancellationToken cancellationToken = default)
    {
        var (storageFiles, localPaths) = OpenSourceFiles(localFilePaths);
        var uploadResult = await polyStore.UploadAsync(storageFiles, stream.StorageInstanceName, cancellationToken);
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
            var localDirectory = stream.Folder.AbsolutePath;
            var remoteUrlsFilePath = stream.Folder.RemoteUrlsPath;
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

    public async Task<bool> FinalizeStorageAsync(Streams.Stream stream, CancellationToken cancellationToken)
    {
        var localDirectory = stream.Folder.AbsolutePath;
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
            return false;

        var remoteUrlsFilePath = stream.Folder.RemoteUrlsPath;
        if (!File.Exists(remoteUrlsFilePath))
        {
            logger.LogWarning(
                "Cannot finalize storage for stream '{StreamId}': '{FileName}' not found.",
                stream.Id, StreamFolder.RemoteUrlsFile);
            return false;
        }

        var playlistPath = stream.Folder.PlaylistPath;
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
        Streams.Stream stream,
        CancellationToken cancellationToken = default)
    {
        var urlFilePath = stream.Folder.RemoteUrlsPath;
        if (stream.StorageLocation == StorageLocation.Remote && File.Exists(urlFilePath))
        {
            var batch = new List<string>(200);

            using (var reader = new StreamReader(urlFilePath))
            {
                string? line;
                while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                {
                    var url = ExtractUrl(line);
                    if (string.IsNullOrWhiteSpace(url))
                        continue;

                    batch.Add(url);

                    if (batch.Count >= 200)
                    {
                        var deleteResult = await polyStore.DeleteAsync(
                            stream.StorageInstanceName!, batch, cancellationToken);
                        if (deleteResult.IsFailure)
                        {
                            logger.LogError("Batch delete failed for stream '{StreamId}': {Error}",
                                stream.Id, deleteResult.Error);
                        }
                        batch.Clear();
                    }
                }
            }

            if (batch.Count > 0)
            {
                var deleteResult = await polyStore.DeleteAsync(
                    stream.StorageInstanceName!, batch, cancellationToken);
                if (deleteResult.IsFailure)
                {
                    logger.LogError("Batch delete failed for stream '{StreamId}': {Error}",
                        stream.Id, deleteResult.Error);
                }
            }
        }

        await dataStore.ExecuteAsync(async () => await dataStore.DeleteAsync<Streams.Stream, string>(stream.Id));

        var localDirectory = stream.Folder.AbsolutePath;
        await IOUtils.DeleteDirectoryAsync(localDirectory);

        var channelDirectory = Path.GetDirectoryName(localDirectory)!;
        if (!Directory.EnumerateFileSystemEntries(channelDirectory).Any())
            await IOUtils.DeleteDirectoryAsync(channelDirectory);

        if (_streamLocks.TryRemove(stream.Id, out var sem))
            sem.Dispose();

        return true;
    }

    private static string? ExtractUrl(string line)
    {
        var trimmed = line.AsSpan().Trim();
        if (trimmed.IsEmpty)
            return null;

        Span<Range> ranges = stackalloc Range[2];
        int count = trimmed.Split(ranges, '\t', StringSplitOptions.RemoveEmptyEntries);

        return count == 2 ? trimmed[ranges[1]].ToString() : line;
    }

    private static (FilePayload[] Files, string[] Paths) OpenSourceFiles(string[] localFilePaths)
    {
        var storageFiles = new FilePayload[localFilePaths.Length];
        for (var i = 0; i < localFilePaths.Length; i++)
        {
            var path = localFilePaths[i];
            storageFiles[i] = new FilePayload(path, ResolveContentType(path), new FileStream(path, FileReadOptions));
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