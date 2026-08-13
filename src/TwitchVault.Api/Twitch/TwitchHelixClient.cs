using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Twitch;

public sealed class TwitchHelixClient(
    IHttpClientFactory httpClientFactory,
    SettingsService settingsService,
    IOptions<PathsOptions> pathsOptions,
    ILogger<TwitchHelixClient> logger)
{
    private const string HelixSubscriptionUrl = "https://api.twitch.tv/helix/eventsub/subscriptions";
    private const string TokenUrl = "https://id.twitch.tv/oauth2/token";

    private TwitchOptions Options => settingsService.Settings.Twitch;

    private string WebhookSecret => Options.Secret;
    private string WebhookUrl
        => $"{pathsOptions.Value.BaseUrl.TrimEnd('/')}{Options.WebhookPath}";

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _appAccessToken;
    private DateTime _tokenExpiresAt = DateTime.UtcNow.AddMinutes(-60);

    private readonly object _rateLimitLock = new();
    private int _rateLimitLimit;
    private int? _rateLimitRemaining;
    private long _rateLimitReset;


    public async IAsyncEnumerable<Subscription> GetEventSubSubscriptionsAsync(
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

    private async Task<string> GetAppAccessTokenAsync(CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_appAccessToken is not null && DateTime.UtcNow < _tokenExpiresAt.AddSeconds(-60))
                return _appAccessToken;

            logger.LogInformation("Fetching new Twitch app access token");
            var client = httpClientFactory.CreateClient();

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

    private void UpdateRateLimits(HttpResponseMessage message)
    {
        lock (_rateLimitLock)
        {
            if (message.Headers.TryGetValues("Ratelimit-Limit", out var limitValues) &&
            int.TryParse(limitValues.FirstOrDefault(), out var limit))
            {
                _rateLimitLimit = limit;
            }

            if (message.Headers.TryGetValues("Ratelimit-Remaining", out var remainingValues) &&
                int.TryParse(remainingValues.FirstOrDefault(), out var remaining))
            {
                _rateLimitRemaining = remaining;
            }

            if (message.Headers.TryGetValues("Ratelimit-Reset", out var resetValues) &&
                long.TryParse(resetValues.FirstOrDefault(), out var reset))
            {
                _rateLimitReset = reset;
            }
        }
    }

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        long resetUnixTime;

        lock (_rateLimitLock)
        {
            if (!_rateLimitRemaining.HasValue)
                return;

            if (_rateLimitRemaining > 0)
            {
                _rateLimitRemaining--;
                return;
            }

            resetUnixTime = _rateLimitReset;
        }

        var delaySeconds = resetUnixTime - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (delaySeconds > 0)
        {
            logger.LogWarning("Twitch Helix API rate limit active. Waiting {Seconds}s...", delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
        }

        lock (_rateLimitLock)
        {
            _rateLimitRemaining = _rateLimitLimit;
        }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(
        HttpMethod method,
        string url,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        const int maxRetryAttempts = 3;
        HttpResponseMessage response = null!;

        var client = httpClientFactory.CreateClient();

        for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            await WaitForRateLimitAsync(cancellationToken);

            var token = await GetAppAccessTokenAsync(cancellationToken);

            using var request = new HttpRequestMessage(method, url);
            request.Headers.TryAddWithoutValidation("Client-Id", Options.ClientId);
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");

            if (body is not null)
                request.Content = JsonContent.Create(body);

            response = await client.SendAsync(request, cancellationToken);
            UpdateRateLimits(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < maxRetryAttempts)
            {
                logger.LogWarning("Twitch Helix API rate limit (429) hit for request to {Url}. Retrying after reset window...", url);
                response.Dispose();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt < maxRetryAttempts)
            {
                logger.LogWarning("Received 401 from Twitch Helix API — clearing cached token and retrying.");
                await InvalidateTokenAsync(cancellationToken);
                response.Dispose();
                continue;
            }

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