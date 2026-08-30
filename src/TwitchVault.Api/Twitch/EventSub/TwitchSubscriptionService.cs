using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchSubscriptionService(
    TwitchHelixClient twitchHelixClient,
    IDbContextFactory<AppDbContext> contextFactory,
    ILogger<TwitchSubscriptionService> logger)
{
    private const int MaxTotalCost = 10_000;
    private const int MaxChannels = MaxTotalCost / TotalEventsPerChannel;
    private const int TotalEventsPerChannel = 2;
    private const int MaxRetries = 3;

    private readonly ConcurrentDictionary<string, List<Subscription>> _channelSubscriptions = [];

    public async Task InitializeSubscriptionsAsync(CancellationToken cancellationToken)
    {
        await LoadExistingSubscriptionsAsync(cancellationToken);

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var monitoredChannels = await db.Channels
            .AsNoTracking()
            .Monitored()
            .ToListAsync(cancellationToken);

        var channels = monitoredChannels
            .Where(c => !_channelSubscriptions.TryGetValue(c.Id, out var subs) || subs.Count <= TotalEventsPerChannel)
            .ToList();

        await AddChannelsAsync(channels, cancellationToken);
    }

    public async Task ClearAllSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var cleanedCount = 0;
        var failedCount = 0;

        var subscriptions = twitchHelixClient.GetEventSubSubscriptionsAsync(cancellationToken: cancellationToken);

        await foreach (var sub in subscriptions)
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

    public async Task AddChannelsAsync(ICollection<Channel> channels, CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
            return;

        var succeeded = new List<string>();
        var failed = new List<string>();

        foreach (var channel in channels)
        {
            var onlineOk = await AddChannelEventAsync(channel.Id, EventsubConstants.StreamOnline, version: "1", cancellationToken);
            var updateOk = await AddChannelEventAsync(channel.Id, EventsubConstants.ChannelUpdate, version: "2", cancellationToken);

            if (onlineOk == true && updateOk == true)
                succeeded.Add(channel.Name);
            else if (onlineOk == false || updateOk == false)
                failed.Add(channel.Name);

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        LogBatchSubscriptionResult(succeeded, failed);
    }

    public async Task RemoveChannelAsync(Channel channel, CancellationToken cancellationToken)
    {
        if (!_channelSubscriptions.TryGetValue(channel.Id, out var subscriptions))
            return;

        var initialCount = subscriptions.Count;
        var deletedCount = 0;
        foreach (var subscription in subscriptions.ReverseIterator())
        {
            var isRemoved = await RemoveChannelEventAsync(channel.Id, subscription.Type, cancellationToken);
            if (isRemoved == true)
                deletedCount++;
        }

        if (deletedCount < initialCount)
            return;

        logger.LogInformation("Unsubscribed for '{Channel}'", channel.Name);
        _channelSubscriptions.TryRemove(channel.Id, out _);
    }

    public async Task<bool?> AddChannelEventAsync(string channelId, string eventType, string version, CancellationToken cancellationToken)
    {
        if (!_channelSubscriptions.ContainsKey(channelId) && _channelSubscriptions.Count >= MaxChannels)
            return null;

        if (!_channelSubscriptions.TryGetValue(channelId, out var subscriptions))
            _channelSubscriptions[channelId] = subscriptions = [];

        if (subscriptions.Count >= TotalEventsPerChannel)
            return null;

        if (subscriptions.Exists(i => i.Type == eventType))
            return true;

        var subscription = await CreateEventSubscriptionAsync(channelId, eventType, version, cancellationToken);
        if (subscription is null)
        {
            if (subscriptions.Count == 0)
                _channelSubscriptions.TryRemove(channelId, out _);

            return false;
        }

        subscriptions.Add(subscription.Value);
        return true;
    }

    public async Task<bool?> RemoveChannelEventAsync(string channelId, string eventType, CancellationToken cancellationToken)
    {
        if (!_channelSubscriptions.TryGetValue(channelId, out var subscriptions))
            return null;

        var index = subscriptions.FindIndex(i => i.Type == eventType);
        if (index == -1)
            return false;

        using var response = await twitchHelixClient.DeleteEventSubSubscriptionAsync(subscriptions[index].Id, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Failed to unsubscribe {Type} for channel '{Channel}': {Status}",
                eventType, channelId, response.StatusCode);
            return false;
        }

        subscriptions.RemoveAt(index);

        if (subscriptions.Count == 0)
            _channelSubscriptions.TryRemove(channelId, out _);

        return true;
    }

    private async Task LoadExistingSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var existingSubs = twitchHelixClient.GetEventSubSubscriptionsAsync(cancellationToken: cancellationToken);

        await foreach (var subscription in existingSubs)
        {
            var channelId = subscription.Condition.BroadcasterUserId;
            if (!_channelSubscriptions.TryGetValue(channelId, out var subscriptions))
                _channelSubscriptions[channelId] = subscriptions = [];

            subscriptions.Add(subscription);
        }
    }

    private async Task<Subscription?> CreateEventSubscriptionAsync(
        string channelId,
        string type,
        string version,
        CancellationToken cancellationToken)
    {
        HttpStatusCode statusCode = HttpStatusCode.OK;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            using var response = await twitchHelixClient.CreateEventSubSubscriptionAsync(
                channelId,
                type,
                version,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                statusCode = response.StatusCode;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                continue;
            }

            var message = await response.Content.ReadFromJsonAsync<EventSubSubscriptionResponse>(cancellationToken);
            return message.Data.Length > 0 ? message.Data[0] : null;
        }

        logger.LogError("Failed to subscribe broadcaster '{BroadcasterId}' for event '{Type}': {Status}",
            channelId, type, statusCode);

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