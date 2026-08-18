using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage.Discord;

public readonly record struct DiscordUploadResult(
    [property: JsonPropertyName("id")] string MessageId,
    [property: JsonPropertyName("attachments")] Attachment[] Attachments);

public readonly record struct Attachment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("filename")] string FileName);


[StorageProvider(CloudProviderType.Discord, typeof(DiscordOptions))]
public sealed class DiscordStorageProvider(
    LiveOptions<StorageInstanceOptions> instanceOptions,
    LiveOptions<DiscordOptions> discordOptions,
    IHttpClientFactory clientFactory,
    ILogger<DiscordStorageProvider> logger) : ICloudStorageProvider
{
    private const int MinBulkDeleteSize = 2;
    private const int MaxBulkDeleteSize = 100;
    private static readonly DateTimeOffset DiscordEpoch = new(2015, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan BulkDeleteMaxAge = TimeSpan.FromDays(14);

    private readonly string MessagesUrl = $"https://discord.com/api/v10/channels/{discordOptions.Value.ChannelId}/messages";

    private readonly object _rateLimitLock = new();
    private int _rateLimitLimit;
    private int? _rateLimitRemaining;
    private float _rateLimitResetAfter;

    public StorageInstanceOptions Options => instanceOptions.Value;

    public async Task<Result<IEnumerable<RemoteUrl>>> UploadAsync(
        IReadOnlyList<StorageFile> files,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
            return Enumerable.Empty<RemoteUrl>().ToResult();

        var resultMap = new List<RemoteUrl>(files.Count);

        foreach (var chunk in files.Chunk(discordOptions.Value.MaxAttachmentsPerMessage))
        {
            var chunkResult = await SendMessageAsync(chunk, cancellationToken);
            if (chunkResult.IsFailure)
                return chunkResult.Error;

            var messageId = chunkResult.Value.MessageId;
            foreach (var attachment in chunkResult.Value.Attachments)
            {
                resultMap.Add(new(attachment.FileName, BuildCDNUrl(messageId, attachment.Id)));
            }

            if (files.Count > discordOptions.Value.MaxAttachmentsPerMessage)
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        return resultMap;
    }

    public async Task<Result> DeleteAsync(IReadOnlyList<string> remoteUrls, CancellationToken cancellationToken)
    {
        // TODO: continue implementation:
        // single-delete
        // count = 1
        // messages older than 14-days

        var messageIds = remoteUrls
            .Select(ExtractMessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (messageIds.Count == 0)
            return Result.Success;

        /*
            messageIds: 1(bulkEligible) - 99(old) --> DeleteSingle (all messages)
            messageIds: 1(bulkEligible) --> DeleteSingle (1 message)
            messageIds: 30(bulkEligible) - 70(old) --> BulkDeleteMessages (bulkEligible) and DeleteSingle (all messages - bulkEligible)
        
        */
        var bulkDeleted = false;
        var bulkEligibleIds = messageIds.Where(id => !IsOlderThan14Days(id)).ToList();
        if (bulkEligibleIds.Count >= MinBulkDeleteSize)
        {
            var bulkResult = await BulkDeleteMessagesAsync(bulkEligibleIds, cancellationToken);
            if (bulkResult.IsFailure)
                return bulkResult;

            if (bulkEligibleIds.Count >= messageIds.Count)
                return bulkResult;

            bulkDeleted = true;
        }

        var oldMessages = messageIds.AsEnumerable();
        if (bulkDeleted)
            oldMessages = oldMessages.Except(bulkEligibleIds);

        foreach (var id in oldMessages)
        {
            var deleteResult = await DeleteSingleAsync(id, cancellationToken);
            if (deleteResult.IsFailure)
                return deleteResult;
        }
        return Result.Success;
    }

    private async Task<Result<DiscordUploadResult>> SendMessageAsync(
        IReadOnlyList<StorageFile> chunk,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", discordOptions.Value.BotToken);

        using var content = new MultipartFormDataContent();
        request.Content = content;

        for (var i = 0; i < chunk.Count; i++)
        {
            var file = chunk[i];
            var fileContent = new StreamContent(file.Content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType);
            content.Add(fileContent, $"files[{i}]", file.FileName);
        }

        try
        {
            await WaitForRateLimitAsync(cancellationToken);
            var response = await SendRequestAsync(request, cancellationToken);
            if (response.IsFailure)
                return response.Error;

            var uploadResult = await response.Value.Content.ReadFromJsonAsync<DiscordUploadResult>(cancellationToken);
            response.Value.Dispose();
            return uploadResult;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error uploading to Discord.");
            return Error.Failure($"Unexpected error uploading to Discord: {ex.Message}");
        }
    }

    private async Task<Result> BulkDeleteMessagesAsync(List<string> messageIds, CancellationToken cancellationToken)
    {
        foreach (var chunk in messageIds.Chunk(MaxBulkDeleteSize))
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{MessagesUrl}/bulk-delete");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bot", discordOptions.Value.BotToken);
            request.Content = JsonContent.Create(new { messages = chunk });

            var response = await SendRequestAsync(request, cancellationToken);
            if (response.IsFailure && response.Error.Type == ErrorType.NotFound)
                continue;

            if (response.IsFailure)
                return response.Error;

            response.Value.Dispose();
        }

        return Result.Success;
    }

    private async Task<Result> DeleteSingleAsync(string messageId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{MessagesUrl}/{messageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue(discordOptions.Value.UserToken);

        var response = await SendRequestAsync(request, cancellationToken);
        if (response.IsFailure && response.Error.Type == ErrorType.NotFound)
            return Result.Success;

        response.Value.Dispose();
        return response;
    }

    private async Task<Result<HttpResponseMessage>> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var client = clientFactory.CreateClient();
        var response = await client.SendAsync(request, cancellationToken);
        UpdateRateLimits(response.Headers);
        if (response.IsSuccessStatusCode)
            return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError("Discord API returned {StatusCode}. Body: {Body}",
            response.StatusCode, body);

        response.Dispose();
        return Error.Failure($"Discord API error {response.StatusCode}: {body}");
    }

    private void UpdateRateLimits(HttpResponseHeaders headers)
    {
        lock (_rateLimitLock)
        {
            if (headers.TryGetValues("x-ratelimit-limit", out var limitValues) &&
            int.TryParse(limitValues.FirstOrDefault(), out var limit))
            {
                _rateLimitLimit = limit;
            }

            if (headers.TryGetValues("x-ratelimit-remaining", out var remainingValues) &&
                int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
            {
                _rateLimitRemaining = remaining;
            }

            if (headers.TryGetValues("x-ratelimit-reset-after", out var resetValues) &&
                float.TryParse(resetValues.FirstOrDefault(), out var reset))
            {
                _rateLimitResetAfter = reset;
            }
        }
    }

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        lock (_rateLimitLock)
        {
            if (!_rateLimitRemaining.HasValue)
                return;

            if (_rateLimitRemaining > 0)
            {
                _rateLimitRemaining--;
                return;
            }
        }

        logger.LogWarning("Discord API rate limit active. Waiting {Seconds}s...", _rateLimitResetAfter);
        await Task.Delay(TimeSpan.FromSeconds(_rateLimitResetAfter + 10), cancellationToken);

        lock (_rateLimitLock)
        {
            _rateLimitRemaining = _rateLimitLimit;
        }
    }

    private static bool IsOlderThan14Days(string messageId)
    {
        // The first 22 bits of a Discord Snowflake contain internal worker/process/counter IDs
        const int SnowflakeTimestampBitShift = 22;

        if (!ulong.TryParse(messageId, out var snowflakeId))
            return true;

        var millisecondsSinceDiscordEpoch = (long)(snowflakeId >> SnowflakeTimestampBitShift);
        var messageCreatedAt = DiscordEpoch.AddMilliseconds(millisecondsSinceDiscordEpoch);

        return DateTimeOffset.UtcNow - messageCreatedAt >= BulkDeleteMaxAge;
    }

    private string BuildCDNUrl(string messageId, string attachmentId) => $"{discordOptions.Value.CDNHost}/{messageId}/{attachmentId}";

    private static string ExtractMessageId(string remoteKey)
    {
        if (string.IsNullOrWhiteSpace(remoteKey))
            return string.Empty;

        return remoteKey.Split('/')[^2];
    }
}