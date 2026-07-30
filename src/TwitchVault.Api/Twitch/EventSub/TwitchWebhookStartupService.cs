using TwitchLib.EventSub.Webhooks.Core;
using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchWebhookStartupService(
    IEventSubWebhooks eventSubWebhooks,
    TwitchSubscriptionService subscriptionService,
    RecordingOrchestrator orchestrator,
    EventBus eventBus,
    ILogger<TwitchWebhookStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        eventSubWebhooks.StreamOnline += async (s, e) =>
        {
            var channelId = e.Payload.Event.BroadcasterUserId;
            var channelName = e.Payload.Event.BroadcasterUserLogin;
            await orchestrator.HandleStreamOnlineAsync(channelId, channelName);
        };

        eventSubWebhooks.ChannelUpdate += async (s, e) =>
        {
            var channelId = e.Payload.Event.BroadcasterUserId;
            var title = e.Payload.Event.Title;
            var categoryId = e.Payload.Event.CategoryId;
            await eventBus.PublishAsync(new ChannelUpdateEvent(channelId, title, categoryId));
        };

        try
        {
            await subscriptionService.InitializeSubscriptionsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to sync EventSub webhook subscriptions on startup.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}