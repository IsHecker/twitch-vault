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

    public async Task<int?> GetEventSubsCountAsync(CancellationToken cancellationToken)
    {
        const string url = $"{HelixSubscriptionUrl}?status=enabled";
        using var response = await SendHelixRequestAsync(HttpMethod.Get, url, null, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var subs = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        return subs?.RootElement.GetProperty("total_cost").GetInt32();
    }

    public async Task<EventSubSubscriptionResponse?> GetEventSubSubscriptionsAsync(string status, CancellationToken cancellationToken)
    {
        string url = $"{HelixSubscriptionUrl}?status={status}";
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

    private Task<HttpResponseMessage> SendHelixRequestAsync(
        HttpMethod method,
        string url,
        object? body = null,
        CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Client-Id", Options.ClientId);

        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {Options.Authorization}");

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return httpClient.SendAsync(request, cancellationToken);
    }

    // private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
    // {
    //     var twitch = settingsService.Settings.Twitch;

    //     var request = new HttpRequestMessage(method, url);
    //     request.Headers.Add("Client-Id", twitch.ClientId);
    //     request.Headers.Authorization = new AuthenticationHeaderValue(
    //         "Bearer", twitch.Authorization.Replace("OAuth ", ""));

    //     if (body is not null)
    //         request.Content = JsonContent.Create(body);

    //     return request;
    // }
}