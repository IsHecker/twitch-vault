using System.Collections.Concurrent;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Events;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchSubscriptionService(
    TwitchHelixClient twitchHelixClient,
    ChannelRepository channelRepository,
    ILogger<TwitchSubscriptionService> logger)
{
    private const int MaxChannels = 5;
    private const int MaxRetries = 3;

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

    public async Task ClearSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var statuses = new[] { "enabled", "websocket_connection_closed" };

        foreach (var status in statuses)
        {
            var response = await twitchHelixClient.GetEventSubSubscriptionsAsync(status, cancellationToken);
            if (response is null || response.Value.Data is null)
                continue;

            foreach (var sub in response.Value.Data)
            {
                if (string.IsNullOrEmpty(SessionId) || sub.Transport.SessionId == SessionId)
                    continue;

                await twitchHelixClient.DeleteEventSubSubscriptionAsync(sub.Id, cancellationToken);
                _subscriptionIds.TryRemove(sub.Id, out _);

                logger.LogInformation("Cleaned up hanging subscription for channel {ChannelId} (Status: {Status})",
                   sub.Condition.BroadcasterUserId, status);
            }
        }
    }

    public async Task SubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (_subscriptionIds.Count >= MaxChannels)
            return;

        if (SessionId is null)
            return;

        if (_subscriptionIds.ContainsKey(channel.ChannelId))
            return;

        await SubscribeAsync(channel, "stream.online", version: "1", cancellationToken);
        await Task.Delay(500, cancellationToken);
        await SubscribeAsync(channel, ChannelUpdateEvent.EventName, version: "2", cancellationToken);
    }

    public async Task UnsubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (!_subscriptionIds.TryGetValue(channel.ChannelId, out var ids))
            return;

        foreach (var id in ids)
        {
            using var response = await twitchHelixClient.DeleteEventSubSubscriptionAsync(id, cancellationToken);

            if (response.IsSuccessStatusCode)
                logger.LogInformation("Unsubscribed {SubId} for channel '{Channel}'", id, channel.Name);
            else
                logger.LogError("Failed to unsubscribe {SubId} for channel '{Channel}': {Status}",
                    id, channel.Name, response.StatusCode);
        }

        _subscriptionIds.Remove(channel.ChannelId, out _);
    }

    private async Task SubscribeAsync(
        Channel channel,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var response = await twitchHelixClient.CreateEventSubSubscriptionAsync(
                channel.ChannelId,
                SessionId,
                type,
                version,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var shit = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "Subscription attempt {Attempt}/{MaxRetries} failed for {Channel} ({Type}): {Status}",
                    attempt, MaxRetries, channel.Name, type, response.StatusCode);

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            var message = await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);
            if (!_subscriptionIds.TryGetValue(channel.ChannelId, out var ids))
                _subscriptionIds[channel.ChannelId] = ids = [];

            if (message.Data.Length > 0)
            {
                ids.Add(message.Data[0].Id);
                logger.LogInformation("Subscribed to {Type} for {Channel}", type, channel.Name);
            }
            return;
        }

        logger.LogError("All subscription attempts failed for {Channel} ({Type}). Giving up.", channel.Name, type);
    }
}