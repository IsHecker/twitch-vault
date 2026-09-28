using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Twitch;

public sealed class TwitchHelixClient(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<TwitchOptions> twitchOptions,
    IOptions<PathsOptions> pathsOptions,
    ILogger<TwitchHelixClient> logger)
{
    private const string HelixSubscriptionUrl = "https://api.twitch.tv/helix/eventsub/subscriptions";
    private const string HelixStreamsUrl = "https://api.twitch.tv/helix/streams";
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";

    private TwitchOptions Options => twitchOptions.CurrentValue;

    private string WebhookSecret => Options.Secret;
    private string WebhookUrl
        => $"{pathsOptions.Value.BaseUrl.TrimEnd('/')}{Options.WebhookPath}";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _appAccessToken;
    private DateTime _tokenExpiresAt = DateTime.UtcNow.AddMinutes(-60);

    public async IAsyncEnumerable<EventSubSubscription> GetEventSubSubscriptionsAsync(
        string status = "",
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;

        do
        {
            var url = $"{HelixSubscriptionUrl}?status={status}&after={cursor}";

            using var response = await SendRequestAsync(HttpMethod.Get, url, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
                yield break;

            var result = await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);

            if (result.Data == null || result.Data.Length == 0)
                yield break;

            foreach (var sub in result.Data)
            {
                yield return sub;
            }

            cursor = result.Pagination?.Cursor;

        } while (!string.IsNullOrEmpty(cursor) && !cancellationToken.IsCancellationRequested);
    }

    public Task<HttpResponseMessage> CreateEventSubSubscriptionAsync(
        string channelId,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            type,
            version,
            condition = new { broadcaster_user_id = channelId },
            transport = new { method = "webhook", callback = WebhookUrl, secret = WebhookSecret }
        };

        return SendRequestAsync(HttpMethod.Post, HelixSubscriptionUrl, payload, cancellationToken);
    }

    public Task<HttpResponseMessage> DeleteEventSubSubscriptionAsync(string id, CancellationToken cancellationToken)
    {
        string url = $"{HelixSubscriptionUrl}?id={id}";
        return SendRequestAsync(HttpMethod.Delete, url, null, cancellationToken);
    }

    public async Task<List<(string UserId, string UserLogin, int ViewerCount)>> GetLiveStreamsAsync(
        int count,
        string language = "",
        int minViewers = 0,
        int maxViewers = int.MaxValue,
        int maxPages = 20,
        CancellationToken cancellationToken = default)
    {
        var results = new List<(string UserId, string UserLogin, int ViewerCount)>(count);
        string? cursor = null;
        int page = 0;

        var baseUrl = $"{HelixStreamsUrl}?first=100&type=live";
        if (!string.IsNullOrWhiteSpace(language))
            baseUrl += $"&language={Uri.EscapeDataString(language)}";

        while (results.Count < count && page < maxPages)
        {
            var url = baseUrl;
            if (!string.IsNullOrEmpty(cursor))
                url += $"&after={cursor}";

            using var response = await SendRequestAsync(HttpMethod.Get, url, null, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("GetLiveStreams page {Page} failed with status {Status}.", page + 1, response.StatusCode);
                break;
            }

            var result = await response.Content.ReadFromJsonAsync<HelixStreamsResponse>(cancellationToken);
            if (result is null || result.Data.Length == 0)
                break;

            foreach (var stream in result.Data)
            {
                if (results.Count >= count)
                    break;

                if (stream.ViewerCount >= minViewers && stream.ViewerCount <= maxViewers)
                    results.Add((stream.UserId, stream.UserLogin, stream.ViewerCount));
            }

            // Since Helix returns streams in descending viewer count order,
            // once the entire page is below minViewers we can stop early.
            if (result.Data.All(s => s.ViewerCount < minViewers))
                break;

            cursor = result.Pagination?.Cursor;
            if (string.IsNullOrEmpty(cursor))
                break;

            page++;
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        logger.LogDebug(
            "GetLiveStreams: collected {Count} streams in {Pages} page(s) (viewers: {Min}–{Max}).",
            results.Count, page + 1, minViewers, maxViewers == int.MaxValue ? "∞" : maxViewers.ToString());

        return results;
    }

    private sealed record HelixStreamsResponse(
        HelixStreamEntry[] Data,
        HelixPagination? Pagination);

    private sealed record HelixStreamEntry(
        [property: System.Text.Json.Serialization.JsonPropertyName("user_id")] string UserId,
        [property: System.Text.Json.Serialization.JsonPropertyName("user_login")] string UserLogin,
        [property: System.Text.Json.Serialization.JsonPropertyName("viewer_count")] int ViewerCount);

    private sealed record HelixPagination(
        [property: System.Text.Json.Serialization.JsonPropertyName("cursor")] string? Cursor);

    private async Task<string> GetAppAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_appAccessToken is not null && DateTime.UtcNow < _tokenExpiresAt.AddSeconds(-60))
                return _appAccessToken;

            logger.LogInformation("Fetching new Twitch app access token");
            using var client = httpClientFactory.CreateClient(nameof(TwitchHelixClient));

            using var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.ClientId,
                ["client_secret"] = Options.Secret,
                ["grant_type"] = "client_credentials"
            });

            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken)
                ?? throw new InvalidOperationException("Empty token response from Twitch.");

            _appAccessToken = json.RootElement.GetProperty("access_token").GetString()!;
            var expiresIn = json.RootElement.GetProperty("expires_in").GetInt32();
            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);
            return _appAccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string url,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var client = httpClientFactory.CreateClient(nameof(TwitchHelixClient));
        var token = await GetAppAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Client-Id", Options.ClientId);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

        if (body is not null)
            request.Content = JsonContent.Create(body);

        var response = await client.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("Received 401 from Twitch Helix API — clearing cached token and retrying.");
            await InvalidateTokenAsync(cancellationToken);
            return response;
        }

        return response;
    }

    private async Task InvalidateTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            _appAccessToken = null;
        }
        finally
        {
            _tokenLock.Release();
        }
    }
}