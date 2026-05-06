using System.Collections.Concurrent;
using System.Net.Http.Headers;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.Twitch.TwitchEventSub;

public sealed class TwitchSubscriptionService(
    HttpClient httpClient,
    SettingsService settingsService,
    ChannelRepository channelRepository,
    ILogger<TwitchSubscriptionService> logger)
{
    private const int MaxChannels = 5;
    private const string EventSubUrl = "https://api.twitch.tv/helix/eventsub/subscriptions";
    private readonly ConcurrentDictionary<string, List<string>> _subscriptionIds = [];

    public string SessionId { get; set; } = null!;

    public async Task SubscribeAllAsync(CancellationToken cancellationToken)
    {
        var channels = (await channelRepository.GetAllAsync())
            .Where(c => c.ShouldRecord);

        foreach (var channel in channels)
        {
            await SubscribeChannelAsync(channel, cancellationToken);
            await Task.Delay(200, cancellationToken);
        }
    }

    public void Reset() => _subscriptionIds.Clear();

    public async Task SubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (_subscriptionIds.Count >= MaxChannels)
            return;

        if (SessionId is null || _subscriptionIds.ContainsKey(channel.ChannelId))
            return;

        await SubscribeAsync(channel, StreamOnlineEvent.EventName, version: "1", cancellationToken);
        await SubscribeAsync(channel, ChannelUpdateEvent.EventName, version: "2", cancellationToken);
    }

    public async Task UnsubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (!_subscriptionIds.TryGetValue(channel.ChannelId, out var ids))
            return;

        foreach (var id in ids)
        {
            using var request = CreateRequest(HttpMethod.Delete, $"{EventSubUrl}?id={id}");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                logger.LogInformation("Unsubscribed {SubId} for channel {Channel}", id, channel.Name);
            else
                logger.LogError("Failed to unsubscribe {SubId}: {Status}", id, response.StatusCode);
        }

        _subscriptionIds.Remove(channel.ChannelId, out _);
    }

    private async Task SubscribeAsync(
        Channel channel,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            type,
            version,
            condition = new { broadcaster_user_id = channel.ChannelId },
            transport = new { method = "websocket", session_id = SessionId }
        };

        using var request = CreateRequest(HttpMethod.Post, EventSubUrl, payload);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Failed subscription for {Channel} ({Type}): {Status}", channel.Name, type, response.StatusCode);
            return;
        }

        var message = await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);

        if (!_subscriptionIds.TryGetValue(channel.ChannelId, out var ids))
            _subscriptionIds[channel.ChannelId] = ids = [];

        ids.Add(message.Data[0].Id);
        logger.LogInformation("Subscribed to {Type} for {Channel}", type, channel.Name);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
    {
        var twitch = settingsService.Settings.Twitch;

        var request = new HttpRequestMessage(method, url);
        request.Headers.Add("Client-Id", twitch.ClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", twitch.Authorization.Replace("OAuth ", ""));

        if (body is not null)
            request.Content = JsonContent.Create(body);

        return request;
    }
}