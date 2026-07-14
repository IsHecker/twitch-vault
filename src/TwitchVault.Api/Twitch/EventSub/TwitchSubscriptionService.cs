using System.Collections.Concurrent;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Events;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchSubscriptionService(
    TwitchHelixClient twitchHelixClient,
    IChannelRepository channelRepository,
    ILogger<TwitchSubscriptionService> logger)
{
    private const int MaxChannels = 5;
    private const int MaxRetries = 3;

    private readonly ConcurrentDictionary<string, List<string>> _subscriptionIds = [];
    public string SessionId { get; set; } = null!;

    public async Task SubscribeChannelsAsync(CancellationToken cancellationToken)
    {
        var channels = (await channelRepository.GetAllAsync())
            .Where(c => c.ShouldRecord)
            .ToList();

        await SubscribeChannelsAsync(channels, cancellationToken);
    }

    public async Task ClearSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var cleanedCount = 0;
        var failedCount = 0;

        var subscriptions = await twitchHelixClient.GetEventSubSubscriptionsAsync(cancellationToken);
        if (subscriptions is null || subscriptions.Value.Data is null)
            return;

        foreach (var sub in subscriptions.Value.Data)
        {
            if (string.IsNullOrEmpty(SessionId) || sub.Transport.SessionId == SessionId)
                continue;

            using var response = await twitchHelixClient.DeleteEventSubSubscriptionAsync(sub.Id, cancellationToken);
            _subscriptionIds.TryRemove(sub.Id, out _);

            if (response.IsSuccessStatusCode)
                cleanedCount++;
            else
                failedCount++;

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        logger.LogInformation("Cleaned up {Count} subscriptions, {Failed} failed", cleanedCount, failedCount);
    }

    public async Task UnsubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (!_subscriptionIds.TryGetValue(channel.Id, out var ids))
            return;

        foreach (var id in ids)
        {
            using var response = await twitchHelixClient.DeleteEventSubSubscriptionAsync(id, cancellationToken);

            if (response.IsSuccessStatusCode)
                logger.LogDebug("Unsubscribed {SubId} for channel '{Channel}'", id, channel.Name);
            else
                logger.LogError("Failed to unsubscribe {SubId} for channel '{Channel}': {Status}",
                    id, channel.Name, response.StatusCode);
        }

        _subscriptionIds.Remove(channel.Id, out _);
    }

    public async Task SubscribeChannelsAsync(ICollection<Channel> channels, CancellationToken cancellationToken)
    {
        var succeeded = new List<string>();
        var failed = new List<string>();

        foreach (var channel in channels)
        {
            var result = await TrySubscribeChannelAsync(channel, cancellationToken);

            if (result is true)
                succeeded.Add(channel.Name);
            else if (result is false)
                failed.Add(channel.Name);

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }

        LogBatchSubscriptionResult(succeeded, failed);
    }

    private async Task<bool?> TrySubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (_subscriptionIds.Count >= MaxChannels)
            return null;

        if (SessionId is null)
            return null;

        if (_subscriptionIds.ContainsKey(channel.Id))
            return null;

        var onlineId = await SubscribeAsync(channel, "stream.online", version: "1", cancellationToken);
        var updateId = await SubscribeAsync(channel, ChannelUpdateEvent.EventName, version: "2", cancellationToken);

        if (!_subscriptionIds.TryGetValue(channel.Id, out var ids))
            _subscriptionIds[channel.Id] = ids = [];

        if (onlineId is not null)
            ids.Add(onlineId);

        if (updateId is not null)
            ids.Add(updateId);

        return onlineId is not null && updateId is not null;
    }

    private async Task<string?> SubscribeAsync(
        Channel channel,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var response = await twitchHelixClient.CreateEventSubSubscriptionAsync(
                channel.Id,
                SessionId,
                type,
                version,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                continue;
            }

            var message = await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);
            return message.Data.Length > 0 ? message.Data[0].Id : null;
        }

        return null;
    }

    private void LogBatchSubscriptionResult(ICollection<string> succeeded, ICollection<string> failed)
    {
        if (succeeded.Count > 0)
            logger.LogInformation("Subscribed to {Channels}", string.Join(", ", succeeded));

        if (failed.Count > 0)
            logger.LogError("Subscription Failed for {Channels}", string.Join(", ", failed));
    }
}