using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.Discord;

public readonly record struct DiscordUploadResult(
    [property: JsonPropertyName("id")] string MessageId,
    [property: JsonPropertyName("attachments")] Attachment[] Attachments);

public readonly record struct Attachment(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("filename")] string FileName);

public sealed class DiscordClient(
    HttpClient httpClient,
    IOptions<DiscordOptions> options,
    ILogger<DiscordClient> logger)
{
    private readonly string MessagesUrl = $"https://discord.com/api/v10/channels/{options.Value.ChannelId}/messages";

    private readonly DiscordOptions _options = options.Value;

    private readonly object _rateLimitLock = new();
    private int _rateLimitLimit;
    private int? _rateLimitRemaining;
    private float _rateLimitResetAfter;

    public async Task<Result<DiscordUploadResult>> UploadAsync(
        string[] filePaths,
        CancellationToken cancellationToken = default)
    {
        await WaitForRateLimitAsync(cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, MessagesUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.BotToken);

        using var content = new MultipartFormDataContent();
        request.Content = content;

        for (int i = 0; i < filePaths.Length; i++)
        {
            string filePath = filePaths[i];

            if (!File.Exists(filePath))
                return Error.NotFound($"Segment file not found: '{filePath}'.");

            var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 81_920, useAsync: true);

            var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(ResolveContentType(filePath));
            content.Add(fileContent, $"files[{i}]", Path.GetFileName(filePath));
        }

        try
        {
            var response = await SendRequestAsync(request, cancellationToken);
            if (response.IsFailure)
                return response.Error;

            return await response.Value.Content.ReadFromJsonAsync<DiscordUploadResult>(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error uploading to Discord.");
            return Error.Failure($"Unexpected error uploading to Discord: {ex.Message}");
        }
    }

    public async Task<Result> DeleteAsync(string messageId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{MessagesUrl}/{messageId}");
        request.Headers.Authorization = new AuthenticationHeaderValue(_options.UserToken);

        var response = await SendRequestAsync(request, cancellationToken);

        if (response.IsFailure && response.Error.Type == ErrorType.NotFound)
            return Result.Success;

        return response;
    }

    public async Task<Result> BulkDeleteMessagesAsync(IEnumerable<string> messageIds, CancellationToken cancellationToken)
    {
        var idList = messageIds.Distinct().ToList();
        if (idList.Count == 0)
            return Result.Success;

        if (idList.Count == 1)
            return await DeleteAsync(idList[0], cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{MessagesUrl}/bulk-delete");
        request.Headers.Authorization = new AuthenticationHeaderValue(_options.UserToken);

        request.Content = JsonContent.Create(new { messages = idList });

        var response = await SendRequestAsync(request, cancellationToken);

        if (response.IsFailure && response.Error.Type == ErrorType.NotFound)
            return Result.Success;

        return response;
    }

    private async Task<Result<HttpResponseMessage>> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);

        UpdateRateLimits(response.Headers);

        if (response.IsSuccessStatusCode)
            return response;

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError("Discord API returned {StatusCode}. Body: {Body}",
            response.StatusCode, body);

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