using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quartz;
using TwitchVault.Api.Events;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Twitch.EventSub;

[DisallowConcurrentExecution]
public class TwitchEventSubJob(
    TwitchWebSocketClient wsClient,
    ITwitchGqlClient twitchGqlClient,
    TwitchHelixClient twitchHelixClient,
    TwitchSubscriptionService subscriptionService,
    IChannelRepository channelRepository,
    EventBus eventBus,
    RecordingOrchestrator streamController,
    ILogger<TwitchEventSubJob> logger) : IJob
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task Execute(IJobExecutionContext context)
    {
        var cancellationToken = context.CancellationToken;
        var channels = await channelRepository.GetAllAsync();

        if (channels.Count == 0 || !channels.Any(c => c.ShouldRecord))
            return;

        logger.LogInformation("Twitch EventSub job is starting/restarting...");
        try
        {
            await wsClient.ConnectAsync(cancellationToken);
            await RunLoopAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Twitch EventSub job. Restarting...");
        }
        finally
        {
            await wsClient.DisposeAsync();
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var receiveTask = ReceiveLoopAsync(heartbeatCts.Token);
        var heartbeatTask = wsClient.MonitorHeartbeatAsync(heartbeatCts.Token);
        var completedTask = await Task.WhenAny(receiveTask, heartbeatTask);

        await heartbeatCts.CancelAsync();
        await completedTask;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (wsClient.State == WebSocketState.Open)
        {
            var json = await wsClient.ReceiveAsync(cancellationToken);
            if (json is null)
                break;

            wsClient.ResetHeartbeat();
            var message = JsonSerializer.Deserialize<EventSubMessage>(json, JsonOptions);
            await HandleMessageAsync(message, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(EventSubMessage message, CancellationToken cancellationToken)
    {
        switch (message.Metadata.MessageType)
        {
            case "session_keepalive":
                return;

            case "session_welcome":
                if (subscriptionService.SessionId != message.Payload.Session.Id)
                    await HandleWelcomeAsync(
                        message.Payload.Session.Id,
                        message.Payload.Session.KeepaliveTimeoutSeconds,
                        cancellationToken);

                await wsClient.CloseOldConnectionAsync();

                var subsCount = await twitchHelixClient.GetEventSubsCountAsync(cancellationToken);
                logger.LogInformation("Active EventSub Subscriptions: {count}", subsCount);
                return;

            case "session_reconnect":
                logger.LogInformation("Session reconnecting to {Url}", message.Payload.Session.ReconnectUrl);
                await wsClient.ReconnectAsync(message.Payload.Session.ReconnectUrl!, cancellationToken);
                return;

            case "notification":
                if (message.Payload.Subscription.Type == "stream.online")
                    await PublishStreamOnlineAsync(message.Payload.Event.RootElement, cancellationToken);
                else if (message.Payload.Subscription.Type == ChannelUpdateEvent.EventName)
                    await PublishChannelUpdateAsync(message.Payload.Event.RootElement);
                else
                    logger.LogWarning("Unhandled event type: {Type}", message.Payload.Subscription.Type);
                return;
        }
    }

    private async Task HandleWelcomeAsync(
        string sessionId,
        int? keepaliveTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        subscriptionService.SessionId = sessionId;
        await subscriptionService.ClearSubscriptionsAsync(cancellationToken);
        await subscriptionService.SubscribeChannelsAsync(cancellationToken);

        wsClient.SetHeartbeat(keepaliveTimeoutSeconds);
    }

    private async Task PublishStreamOnlineAsync(JsonElement e, CancellationToken cancellationToken)
    {
        var channelName = e.GetProperty("broadcaster_user_login").GetString()!;

        var metadata = await twitchGqlClient.GetStreamMetadataAsync(channelName, cancellationToken);
        await streamController.HandleStreamOnlineAsync(
            e.GetProperty("broadcaster_user_id").GetString()!, channelName, metadata.Value);
    }

    private async Task PublishChannelUpdateAsync(JsonElement e) =>
        await eventBus.PublishAsync(new ChannelUpdateEvent(
            ChannelId: e.GetProperty("broadcaster_user_id").GetString()!,
            Title: e.GetProperty("title").GetString()!,
            CategoryName: e.GetProperty("category_name").GetString()!));
}