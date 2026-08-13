using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.CloudStorage.Discord;

public readonly record struct DiscordUploadResult(
    [property: JsonPropertyName("id")] string MessageId,
    [property: JsonPropertyName("attachments")] Attachment[] Attachments);

public readonly record struct Attachment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("filename")] string FileName);


[StorageProvider(CloudProviderType.Discord, typeof(DiscordOptions))]
public sealed class DiscordStorageProvider(
    StorageInstanceOptions instanceOptions,
    DiscordOptions discordOptions,
    IHttpClientFactory clientFactory,
    IDateTimeProvider timeProvider,
    ILogger<DiscordStorageProvider> logger) : ICloudStorageProvider
{
    private readonly TimeSpan BulkDeleteThreshold = TimeSpan.FromDays(14);
    private readonly string MessagesUrl = $"https://discord.com/api/v10/channels/{discordOptions.ChannelId}/messages";

    private readonly object _rateLimitLock = new();
    private int _rateLimitLimit;
    private int? _rateLimitRemaining;
    private float _rateLimitResetAfter;

    public StorageInstanceOptions Options => instanceOptions;

    public async Task<Result<IEnumerable<string>>> UploadAsync(
        IEnumerable<Stream> dataStreams,
        IEnumerable<LocalSegment> segments,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", discordOptions.BotToken);

        using var content = new MultipartFormDataContent();
        request.Content = content;

        var fileIndex = 0;
        foreach (var (stream, segment) in dataStreams.Zip(segments))
        {
            var fileContent = new StreamContent(stream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveContentType(segment.LocalPath));
            content.Add(fileContent, $"files[{fileIndex++}]", Path.GetFileName(segment.LocalPath));
        }

        var responseResult = await SendMessageAsync(request, cancellationToken);
        if (responseResult.IsFailure)
            return responseResult.Error;

        return responseResult.Value.Attachments
            .Select(att => BuildCDNUrl(responseResult.Value.MessageId, att.Id))
            .ToResult();
    }

    public async Task<Result> DeleteBatchAsync(Domain.Stream stream, string playlistContent, CancellationToken cancellationToken)
    {
        var extractionResult = ManifestSegmentExtractor.ExtractAllSegments(playlistContent);
        var remoteUrls = extractionResult.Segments
            .Select(seg => seg.Url)
            .Append(extractionResult.InitSegmentUrl ?? string.Empty);

        var messageIds = remoteUrls
            .Select(ExtractMessageId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

        if (messageIds.Count == 0)
            return Result.Success;

        var uploadedAt = DateTime.Parse(HlsTagReader.ReadTagValue(playlistContent, HlsTags.UploadedTimePrefix));
        if (messageIds.Count > 1 && timeProvider.DateTimeNow < uploadedAt.Add(BulkDeleteThreshold))
            return await BulkDeleteMessagesAsync(messageIds, cancellationToken);

        foreach (var id in messageIds)
        {
            var deleteResult = await DeleteAsync(id, cancellationToken);
            if (deleteResult.IsFailure)
                return deleteResult;
        }

        return Result.Success;
    }

    private async Task<Result<DiscordUploadResult>> SendMessageAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
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

    private async Task<Result> DeleteAsync(string messageId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{MessagesUrl}/{messageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue(discordOptions.UserToken);

        var response = await SendRequestAsync(request, cancellationToken);
        if (response.IsFailure && response.Error.Type == ErrorType.NotFound)
            return Result.Success;

        response.Value.Dispose();
        return response;
    }

    private async Task<Result> BulkDeleteMessagesAsync(IEnumerable<string> messageIds, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MessagesUrl}/bulk-delete");
        request.Headers.Authorization = new AuthenticationHeaderValue(discordOptions.UserToken);
        request.Content = JsonContent.Create(new { messages = messageIds });

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
        await Task.Delay(TimeSpan.FromSeconds(_rateLimitResetAfter), cancellationToken);

        lock (_rateLimitLock)
        {
            _rateLimitRemaining = _rateLimitLimit;
        }
    }

    private string BuildCDNUrl(string messageId, string attachmentId) => $"{discordOptions.CDNHost}/{messageId}/{attachmentId}";

    private static string ExtractMessageId(string remoteKey)
    {
        if (string.IsNullOrWhiteSpace(remoteKey))
            return string.Empty;

        return remoteKey.Split('/')[^2];
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