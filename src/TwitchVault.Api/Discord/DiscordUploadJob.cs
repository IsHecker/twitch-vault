using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Discord;

[DisallowConcurrentExecution]
public sealed class DiscordUploadJob(
    DiscordClient client,
    IStreamRepository streamRepository,
    UploadProgressService progressService,
    SettingsService settingsService,
    IOptions<DiscordOptions> options,
    IWebHostEnvironment env,
    ILogger<DiscordUploadJob> logger) : IJob
{
    private readonly DiscordOptions _options = options.Value;

    public async Task Execute(IJobExecutionContext context)
    {
        if (!settingsService.Settings.BackgroundJobs[JobOptions.DiscordUpload].Enabled)
            return;

        var pendingStreams = (await streamRepository.GetAllAsync())
            .Where(s => s.Status == StreamStatus.Finished
                && (s.Storage == StorageLocation.Local || s.Storage == StorageLocation.Uploading))
            .OrderBy(s => s.StartedAt);

        foreach (var stream in pendingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await UploadStreamSegmentsAsync(stream, context.CancellationToken);
        }
    }

    private async Task UploadStreamSegmentsAsync(Domain.Stream stream, CancellationToken cancellationToken)
    {
        var streamTitle = stream.Chapters[0].Title;
        streamTitle = streamTitle[..Math.Max(1, (int)(streamTitle.Length * 0.8f))] + "...";

        var localDirectory = stream.Folder.GetAbsolutePath(env.ContentRootPath);

        if (!Directory.Exists(localDirectory))
        {
            logger.LogWarning(
                "Upload skipped for '{Title}': local directory '{Dir}' not found.",
                streamTitle, localDirectory);
            return;
        }

        var lastUploadedIndex = progressService.GetLastUploadedSegmentIndex(stream.TwitchStreamId);

        var pendingSegments = Directory.EnumerateFiles(localDirectory, "*")
            .Where(HlsSegmentNaming.IsSegmentFile)
            .OrderBy(HlsSegmentNaming.GetSegmentIndex)
            .Skip(lastUploadedIndex + 1)
            .ToList();

        var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
        var rewrittenPlaylistPath = $"{stream.Folder.RelativePath}/discord-{StreamFolder.PlaylistFile}";
        var playlistRewriter = new HlsPlaylistRewriter(playlistPath, rewrittenPlaylistPath);

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

        logger.LogInformation("Starting Discord upload for '{Title}'. Remaining: {PendingCount}/{TotalCount} segments.",
            streamTitle, pendingSegments.Count, lastUploadedIndex + pendingSegments.Count + 1);

        foreach (var segmentPaths in pendingSegments.Chunk(_options.MaxAttachmentsPerMessage))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await client.UploadAsync(segmentPaths, cancellationToken);
            if (result.IsFailure)
            {
                logger.LogError("Failed to upload segment chunk: {Error}", result.Error);
                return;
            }

            await RewritePlaylistSegments(result.Value, playlistRewriter, cancellationToken);

            lastUploadedIndex += segmentPaths.Length;
            await progressService.SaveProgressAsync(stream.TwitchStreamId, lastUploadedIndex);

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        await playlistRewriter.CompleteAsync(cancellationToken);

        stream.SetStorageLocation(StorageLocation.Both);
        await streamRepository.UpdateAsync(stream);

        DeleteStreamSegments(localDirectory);
        await progressService.RemoveProgressAsync(stream.TwitchStreamId);
        File.Move(rewrittenPlaylistPath, playlistPath, overwrite: true);

        logger.LogInformation("Stream '{Title}' successfully uploaded to Discord.", streamTitle);
    }

    private async Task RewritePlaylistSegments(
        DiscordUploadResult uploadResult,
        HlsPlaylistRewriter playlistRewriter,
        CancellationToken cancellationToken)
    {
        foreach (var attachment in uploadResult.Attachments)
        {
            var segmentUrl = FormatSegmentUrl(uploadResult, attachment);
            await playlistRewriter.RewriteNextSegmentAsync(segmentUrl, cancellationToken);
        }
    }

    private static void DeleteStreamSegments(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*").Where(HlsSegmentNaming.IsSegmentFile))
            File.Delete(file);
    }

    private string FormatSegmentUrl(DiscordUploadResult uploadResult, Attachment attachment)
        => $"{_options.CDNHost}/{uploadResult.MessageId}/{attachment.Id}";
}