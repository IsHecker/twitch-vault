
namespace TwitchVault.Api.Features.Testing.Endpoints;

public class MockTwitchEvents : IDevOnlyEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/twitch").WithTags("Testing");
        group.MapPost("/online", async (
            string channelId,
            string channelName,
            IRecordingOrchestrator recordingOrchestrator) =>
        {
            await recordingOrchestrator.TryStartRecordingAsync(channelId, channelName);
            return Results.Ok($"Sent StreamOnlineEvent for {channelName}");
        });

        group.MapPost("/update", async (string channelId, string title, string categoryId, EventBus bus) =>
        {
            await bus.PublishAsync(channelId, new ChannelUpdateEvent(channelId, title, categoryId));
            return Results.Ok($"Sent ChannelUpdateEvent: {title} | {categoryId}");
        });

        group.MapPost("/clear-events", async (TwitchSubscriptionService service) =>
        {
            await service.ClearAllSubscriptionsAsync(default);
            return Results.Ok();
        });
    }
}