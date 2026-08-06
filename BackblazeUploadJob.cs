using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Backblaze;

[DisallowConcurrentExecution]
public sealed class BackblazeUploadJob(
    BackblazeStorageService storage,
    IStreamRepository streamRepository,
    IOptions<PathsOptions> pathsOptions,
    IWebHostEnvironment env,
    ILogger<BackblazeUploadJob> logger) : IJob
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    public async Task Execute(IJobExecutionContext context)
    {
        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.Finished
                && (s.Storage == StorageLocation.Local || s.Storage == StorageLocation.Uploading))
            .OrderBy(s => s.StartedAt);

        foreach (var stream in pendingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await UploadStreamAsync(stream, context.CancellationToken);
        }
    }

    private async Task UploadStreamAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning(
                "Upload skipped for stream {StreamId}: local directory '{Dir}' not found.",
                stream.TwitchStreamId, localDirectory);
            return;
        }

        if (stream.Storage == StorageLocation.Local)
        {
            stream.SetStorageLocation(StorageLocation.Uploading);
            await streamRepository.UpdateAsync(stream);
        }

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var objectFolder = Path.GetRelativePath(pathsOptions.Value.Streams, stream.Folder.RelativePath);

                logger.LogInformation(
                    "Starting B2 upload for '{Title}' (Folder: '{Folder}').",
                    stream.Chapters[0].Title, objectFolder);

                await storage.UploadDirectoryAsync(localDirectory, objectFolder, cancellationToken);

                stream.SetStorageLocation(StorageLocation.Both);
                await streamRepository.UpdateAsync(stream);

                logger.LogInformation(
                    "Stream {StreamId} successfully uploaded to B2 at prefix '{Prefix}'.",
                    stream.TwitchStreamId, objectFolder);

                return;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                    return;

                if (attempt < MaxRetries)
                {
                    await Task.Delay(RetryDelay, cancellationToken);
                    return;
                }

                logger.LogError(ex,
                    "B2 upload failed permanently for stream {StreamId} after {Max} attempts. " +
                    "Stream reverted to Local; re-run or investigate manually.",
                    stream.TwitchStreamId, MaxRetries);

                stream.SetStorageLocation(StorageLocation.Local);
                await streamRepository.UpdateAsync(stream);
            }
        }
    }
}