using TwitchLib.EventSub.Webhooks.Core;

namespace TwitchVault.Api.Features.Twitch.EventSub;

public sealed class TwitchWebhookStartupService(
    IEventSubWebhooks eventSubWebhooks,
    ITwitchSubscriptionService subscriptionService,
    IRecordingOrchestrator orchestrator,
    EventBus eventBus,
    ILogger<TwitchWebhookStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        eventSubWebhooks.StreamOnline += (s, e) =>
        {
            var channelId = e.Payload.Event.BroadcasterUserId;
            var channelName = e.Payload.Event.BroadcasterUserLogin;
            return HandleStreamOnlineSafeAsync(channelId, channelName);
        };

        eventSubWebhooks.ChannelUpdate += async (s, e) =>
        {
            var channelId = e.Payload.Event.BroadcasterUserId;
            var title = e.Payload.Event.Title;
            var categoryId = e.Payload.Event.CategoryId;
            await eventBus.PublishAsync(channelId, new ChannelUpdateEvent(channelId, title, categoryId));
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

    private async Task HandleStreamOnlineSafeAsync(string channelId, string channelName)
    {
        try
        {
            await orchestrator.TryStartRecordingAsync(channelId, channelName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error handling stream online for {Channel}.", channelName);
        }
    }
}