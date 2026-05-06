using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quartz;
using TwitchVault.Api.Events;
using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Twitch.TwitchEventSub;

[DisallowConcurrentExecution]
public class TwitchEventSubJob(
    TwitchWebSocketClient wsClient,
    TwitchSubscriptionService subscriptionService,
    ChannelRepository channelRepository,
    EventBus eventBus,
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
            await HandleWelcomeAsync(cancellationToken);

            while (wsClient.State == WebSocketState.Open)
            {
                var json = await wsClient.ReceiveAsync(cancellationToken);
                if (json is null)
                    break;

                var message = JsonSerializer.Deserialize<EventSubMessage>(json, JsonOptions);
                await HandleMessageAsync(message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in Twitch EventSub job. Waiting 5s before next attempt.");
        }
        finally
        {
            await wsClient.DisposeAsync();
        }
    }

    private async Task HandleWelcomeAsync(CancellationToken cancellationToken)
    {
        subscriptionService.Reset();
        var json = await wsClient.ReceiveAsync(cancellationToken) ?? throw new InvalidOperationException("Connection closed before welcome.");
        var message = JsonSerializer.Deserialize<EventSubMessage>(json, JsonOptions);

        if (message.Metadata.MessageType != "session_welcome")
            throw new InvalidOperationException($"Expected session_welcome message but received {message.Metadata.MessageType}");

        subscriptionService.SessionId = message.Payload.Session.Id;
        await subscriptionService.SubscribeAllAsync(cancellationToken);
    }

    private async Task HandleMessageAsync(EventSubMessage message, CancellationToken cancellationToken)
    {
        switch (message.Metadata.MessageType)
        {
            case "session_keepalive":
                return;

            case "session_welcome":
                subscriptionService.SessionId = message.Payload.Session.Id;
                return;

            case "session_reconnect":
                logger.LogInformation("Session reconnecting to {Url}", message.Payload.Session.ReconnectUrl);
                await wsClient.ReconnectAsync(message.Payload.Session.ReconnectUrl!, cancellationToken);
                return;

            case "notification":
                if (message.Payload.Subscription.Type == StreamOnlineEvent.EventName)
                {
                    await PublishStreamOnlineAsync(message.Payload.Event.RootElement);
                }
                else if (message.Payload.Subscription.Type == ChannelUpdateEvent.EventName)
                {
                    await PublishChannelUpdateAsync(message.Payload.Event.RootElement);
                }
                else
                {
                    logger.LogWarning("Unhandled event type: {Type}", message.Payload.Subscription.Type);
                }
                return;
        }
    }

    private async Task PublishStreamOnlineAsync(JsonElement e) =>
        await eventBus.PublishAsync(new StreamOnlineEvent(
            ChannelId: e.GetProperty("broadcaster_user_id").GetString()!,
            ChannelName: e.GetProperty("broadcaster_user_login").GetString()!,
            StartedAt: e.GetProperty("started_at").GetDateTime()));

    private async Task PublishChannelUpdateAsync(JsonElement e) =>
        await eventBus.PublishAsync(new ChannelUpdateEvent(
            ChannelId: e.GetProperty("broadcaster_user_id").GetString()!,
            Title: e.GetProperty("title").GetString()!,
            CategoryName: e.GetProperty("category_name").GetString()!));
}