using Microsoft.Extensions.Options;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Discord;

namespace TwitchVault.Api.CloudStorage.Providers;

public sealed class DiscordStorageProvider(DiscordClient client, IOptions<DiscordOptions> options) : ICloudStorageProvider
{
    public CloudProviderType ProviderType => CloudProviderType.Discord;
    public string ProviderInstanceId { get; private set; } = null!;
    public StorageCapabilities Capabilities => new(MaxBatchSize: 10, MaxFileSizeBytes: 10_485_760);

    public void Initialize(string instanceId, Dictionary<string, string> settings)
    {
        ProviderInstanceId = instanceId;
    }

    public async Task<Result<UploadBatchResult>> UploadBatchAsync(
        string[] localFilePaths,
        CancellationToken cancellationToken)
    {
        var uploadResult = await client.UploadAsync(localFilePaths, cancellationToken);
        if (uploadResult.IsFailure)
            return uploadResult.Error;

        var uploaded = uploadResult.Value;
        var list = new List<UploadedSegment>();
        for (int i = 0; i < uploaded.Attachments.Length; i++)
        {
            var url = FormatDiscordUrl(uploaded.MessageId, uploaded.Attachments[i].Id);
            list.Add(new UploadedSegment(localFilePaths[i], url));
        }

        return new UploadBatchResult(list);
    }

    public async Task<Result> DeleteStreamDataAsync(
        string? localPlaylistPath,
        object? deletionProgress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(localPlaylistPath) || !File.Exists(localPlaylistPath))
            return Result.Success; // Nothing to delete if playlist doesn't exist

        var messageIds = ExtractMessageIds(localPlaylistPath);
        if (messageIds.Count == 0)
            return Result.Success;

        // Group into batches of 100 for Discord Bulk Delete API (< 14 days old)
        foreach (var chunk in messageIds.Chunk(100))
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var result = await client.BulkDeleteMessagesAsync(chunk, cancellationToken);
            if (result.IsSuccess)
                continue;

            // If bulk delete failed, try individual delete as fallback
            foreach (var msgId in chunk)
            {
                await client.DeleteAsync(msgId, cancellationToken);
            }
        }
        return Result.Success;
    }

    private static HashSet<string> ExtractMessageIds(string playlistPath)
    {
        var ids = new HashSet<string>();
        foreach (var line in File.ReadLines(playlistPath))
        {
            ids.Add("The message id");
        }
        return ids;
    }

    private string FormatDiscordUrl(string messageId, string attachmentId)
        => $"{options.Value.CDNHost}/{messageId}/{attachmentId}";
}