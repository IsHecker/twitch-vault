using Quartz;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Backblaze;

[DisallowConcurrentExecution]
public sealed class BackblazeUploadJob(
    BackblazeStorageService storage,
    IStreamRepository streamRepository,
    BackblazeUploadProgressService progressService,
    SettingsService settingsService,
    IWebHostEnvironment env,
    ILogger<BackblazeUploadJob> logger) : IJob
{
    private static DateTime? _rateLimitCoolOffUntil;
    private static DateTime? _quotaExceededUntil;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.BackgroundJobs["BackblazeUpload"].Enabled)
            return;

        if (_quotaExceededUntil.HasValue)
        {
            if (DateTime.UtcNow < _quotaExceededUntil.Value)
            {
                logger.LogDebug("daily quota exceeded freeze active until {Until:yyyy-MM-dd HH:mm:ss} UTC.", _quotaExceededUntil.Value);
                return;
            }
            _quotaExceededUntil = null;
        }

        if (_rateLimitCoolOffUntil.HasValue)
        {
            if (DateTime.UtcNow < _rateLimitCoolOffUntil.Value)
            {
                return;
            }
            logger.LogDebug("rate limit cool-off finished at {Until:HH:mm:ss} UTC.", _rateLimitCoolOffUntil.Value);
            _rateLimitCoolOffUntil = null;
        }

        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.Finished
                && (s.Storage == StorageLocation.Local || s.Storage == StorageLocation.Uploading))
            .OrderBy(s => s.StartedAt);

        foreach (var stream in pendingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested || _rateLimitCoolOffUntil.HasValue || _quotaExceededUntil.HasValue)
                break;

            await UploadStreamSegmentsAsync(stream, context.CancellationToken);
        }
    }

    private async Task UploadStreamSegmentsAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var streamTitle = stream.Chapters[0].Title;
        streamTitle = streamTitle[..Math.Max(1, (int)(streamTitle.Length * 0.4f))] + "...";

        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning(
                "Upload skipped for '{Title}': local directory '{Dir}' not found.",
                streamTitle, localDirectory);
            return;
        }

        var lastUploadedIndex = progressService.GetLastUploadedSegmentIndex(stream.TwitchStreamId);

        var pendingSegments = Directory.EnumerateFiles(localDirectory, "*", SearchOption.AllDirectories)
            .Where(HlsSegmentNaming.IsSegmentFile)
            .OrderBy(HlsSegmentNaming.GetSegmentIndex)
            .ToList();

        if (pendingSegments.Count == 0)
        {
            logger.LogInformation("Stream '{Title}' has no segment files to upload.", streamTitle);
            await streamRepository.UpdateAsync(stream);
            await progressService.RemoveProgressAsync(stream.TwitchStreamId);
            return;
        }

        if (stream.Storage == StorageLocation.Local)
        {
            stream.SetStorageLocation(StorageLocation.Uploading);
            await streamRepository.UpdateAsync(stream);
        }

        logger.LogInformation("Starting B2 upload for '{Title}'. Remaining: {PendingCount}/{TotalCount} segments.",
            streamTitle, pendingSegments.Count, lastUploadedIndex + pendingSegments.Count + 1);

        var objectPrefix = stream.Folder.RelativePath;
        foreach (var segmentPath in pendingSegments)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var relativePath = Path.GetRelativePath(localDirectory, segmentPath).Replace('\\', '/');
            var key = $"{objectPrefix.TrimEnd('/')}/{relativePath}";

            var result = await storage.UploadFileAsync(segmentPath, key, cancellationToken);

            if (result.IsSuccess)
            {
                await progressService.SaveProgressAsync(stream.TwitchStreamId, ++lastUploadedIndex);
                continue;
            }

            if (result.Error.Type == ErrorType.TooManyRequests)
            {
                _rateLimitCoolOffUntil = DateTime.UtcNow.AddMinutes(5);
                logger.LogWarning("Backblaze rate limit hit while uploading {File}. Freezing job until {Until:HH:mm:ss} UTC.",
                    relativePath, _rateLimitCoolOffUntil.Value);
            }
            else if (result.Error.Type == ErrorType.Forbidden)
            {
                _quotaExceededUntil = DateTime.UtcNow.Date.AddDays(1);
                logger.LogError("Backblaze daily quota exceeded while uploading {File}. Freezing job until next day ({Until:yyyy-MM-dd HH:mm:ss} UTC).",
                    relativePath, _quotaExceededUntil.Value);
            }
            else
            {
                logger.LogError("Failed to upload segment {File}: {Error}", relativePath, result.Error);
            }

            return;
        }

        stream.SetStorageLocation(StorageLocation.Both);
        await streamRepository.UpdateAsync(stream);
        await progressService.RemoveProgressAsync(stream.TwitchStreamId);

        logger.LogInformation("Stream '{Title}' successfully uploaded to B2 at prefix '{Prefix}'.",
            streamTitle, objectPrefix);
    }
}