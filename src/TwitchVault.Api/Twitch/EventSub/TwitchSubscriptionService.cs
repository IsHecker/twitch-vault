using System.Collections.Concurrent;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchSubscriptionService(
    TwitchHelixClient twitchHelixClient,
    IChannelRepository channelRepository,
    ILogger<TwitchSubscriptionService> logger)
{
    private const int MaxChannels = 10_000 / TotalEventsPerChannel;
    private const int TotalEventsPerChannel = 2;
    private const int MaxRetries = 3;

    private readonly ConcurrentDictionary<string, List<Subscription>> _subscriptionIds = [];

    public async Task InitializeSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await LoadExistingSubscriptionsAsync(cancellationToken);

        var channels = (await channelRepository.GetAllAsync())
            .Where(c => c.ShouldRecord
                && (!_subscriptionIds.TryGetValue(c.Id, out var subs) || subs.Count < TotalEventsPerChannel))
            .ToList();

        await AddChannelsAsync(channels, cancellationToken);
    }

    public async Task ClearAllSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var cleanedCount = 0;
        var failedCount = 0;

        var subscriptions = await twitchHelixClient.GetEventSubSubscriptionsAsync(cancellationToken);
        if (subscriptions is null || subscriptions.Value.Data is null)
            return;

        foreach (var sub in subscriptions.Value.Data)
        {
            using var response = await twitchHelixClient.DeleteEventSubSubscriptionAsync(sub.Id, cancellationToken);

            if (response.IsSuccessStatusCode)
                cleanedCount++;
            else
                failedCount++;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        logger.LogInformation("Cleaned up {Count} subscriptions, {Failed} failed", cleanedCount, failedCount);
    }

    public async Task RemoveChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (!_subscriptionIds.TryGetValue(channel.Id, out var subscriptions))
            return;

        var deletedCount = 0;

        foreach (var subscription in subscriptions)
        {
            using var response = await twitchHelixClient.DeleteEventSubSubscriptionAsync(subscription.Id, cancellationToken);

            if (response.IsSuccessStatusCode)
                deletedCount++;
            else
                logger.LogError("Failed to unsubscribe for channel '{Channel}': {Status}",
                    channel.Name, response.StatusCode);
        }

        if (deletedCount < subscriptions.Count)
            return;

        logger.LogInformation("Unsubscribed for '{Channel}'", channel.Name);
        _subscriptionIds.Remove(channel.Id, out _);
    }

    public async Task AddChannelsAsync(ICollection<Channel> channels, CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
            return;

        var succeeded = new List<string>();
        var failed = new List<string>();

        foreach (var channel in channels)
        {
            var result = await TrySubscribeChannelAsync(channel, cancellationToken);

            if (result is true)
                succeeded.Add(channel.Name);
            else if (result is false)
                failed.Add(channel.Name);

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        LogBatchSubscriptionResult(succeeded, failed);
    }

    private async Task LoadExistingSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var existingSubs = await twitchHelixClient.GetEventSubSubscriptionsAsync(cancellationToken);
        var existingList = existingSubs?.Data ?? [];

        foreach (var subscription in existingList)
        {
            var channelId = subscription.Condition.BroadcasterUserId;
            if (!_subscriptionIds.TryGetValue(channelId, out var subscriptions))
                _subscriptionIds[channelId] = subscriptions = [];

            subscriptions.Add(subscription);
        }
    }

    private async Task<bool?> TrySubscribeChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (!_subscriptionIds.ContainsKey(channel.Id) && _subscriptionIds.Count >= MaxChannels)
            return null;

        if (_subscriptionIds.TryGetValue(channel.Id, out var subscriptions) && subscriptions.Count == TotalEventsPerChannel)
            return null;

        if (!_subscriptionIds.TryGetValue(channel.Id, out subscriptions))
            _subscriptionIds[channel.Id] = subscriptions = [];

        var result = true;

        if (!subscriptions.Exists(i => i.Type == EventsubConstants.StreamOnline))
        {
            var onlineSub = await CreateEventSubscriptionAsync(channel, EventsubConstants.StreamOnline, version: "1", cancellationToken);
            if (onlineSub is not null)
                subscriptions.Add(onlineSub.Value);
            else
                result = false;
        }

        if (!subscriptions.Exists(i => i.Type == EventsubConstants.ChannelUpdate))
        {
            var updateSub = await CreateEventSubscriptionAsync(channel, EventsubConstants.ChannelUpdate, version: "2", cancellationToken);
            if (updateSub is not null)
                subscriptions.Add(updateSub.Value);
            else
                result = false;
        }

        return result;
    }

    private async Task<Subscription?> CreateEventSubscriptionAsync(
        Channel channel,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var response = await twitchHelixClient.CreateEventSubSubscriptionAsync(
                channel.Id,
                type,
                version,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            var message = await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);
            return message.Data.Length > 0 ? message.Data[0] : null;
        }

        return null;
    }

    private void LogBatchSubscriptionResult(ICollection<string> succeeded, ICollection<string> failed)
    {
        if (succeeded.Count > 0)
            logger.LogInformation("Subscribed to {Channels}", string.Join(", ", succeeded));

        if (failed.Count > 0)
            logger.LogError("Failed to subscribe for {Channels}", string.Join(", ", failed));
    }
}