using System.Net;
using System.Text.Json;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Twitch;

public sealed class TwitchHelixClient(
    HttpClient httpClient,
    SettingsService settingsService)
{
    private const string HelixSubscriptionUrl = "https://api.twitch.tv/helix/eventsub/subscriptions";
    private TwitchOptions Options => settingsService.Settings.Twitch;

    private int _rateLimitLimit;
    private int? _rateLimitRemaining;
    private long _rateLimitReset;

    public async Task<int?> GetEventSubsCountAsync(CancellationToken cancellationToken)
    {
        const string url = $"{HelixSubscriptionUrl}?status=enabled";
        using var response = await SendHelixRequestAsync(HttpMethod.Get, url, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var subs = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return subs?.RootElement.GetProperty("total_cost").GetInt32();
    }

    public async Task<EventSubSubscriptionResponse?> GetEventSubSubscriptionsAsync(CancellationToken cancellationToken)
    {
        string url = $"{HelixSubscriptionUrl}?status=enabled";
        using var response = await SendHelixRequestAsync(HttpMethod.Get, url, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);
    }

    public Task<HttpResponseMessage> CreateEventSubSubscriptionAsync(
        string channelId,
        string sessionId,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            type,
            version,
            condition = new { broadcaster_user_id = channelId },
            transport = new { method = "websocket", session_id = sessionId }
        };

        return SendHelixRequestAsync(HttpMethod.Post, HelixSubscriptionUrl, payload, cancellationToken);
    }

    public Task<HttpResponseMessage> DeleteEventSubSubscriptionAsync(string id, CancellationToken cancellationToken)
    {
        string url = $"{HelixSubscriptionUrl}?id={id}";
        return SendHelixRequestAsync(HttpMethod.Delete, url, null, cancellationToken);
    }

    private void UpdateRateLimits(HttpResponseMessage message)
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

    private async Task WaitForRateLimitAsync(CancellationToken cancellationToken)
    {
        if (!_rateLimitRemaining.HasValue ||
            _rateLimitRemaining.Value > 0)
            return;

        var resetUnixTime = _rateLimitReset;
        var currentUnixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var delaySeconds = resetUnixTime - currentUnixTime;

        if (delaySeconds <= 0)
            return;

        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
    }

    private async Task<HttpResponseMessage> SendHelixRequestAsync(
        HttpMethod method,
        string url,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        const int maxRetryAttempts = 3;
        HttpResponseMessage response = null!;

        using var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Client-Id", Options.ClientId);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Options.Authorization}");

        for (int attempt = 1; attempt <= maxRetryAttempts; attempt++)
        {
            await WaitForRateLimitAsync(cancellationToken);

            if (body is not null)
                request.Content = JsonContent.Create(body);

            response = await httpClient.SendAsync(request, cancellationToken);
            UpdateRateLimits(response);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                response.Dispose();
                continue;
            }

            return response;
        }

        return response;
    }
}