using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Testing;

public class MockTwitchEvents : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/twitch").WithTags("Testing");
        group.MapPost("/online", async (
            string channelId,
            string channelName,
            RecordingOrchestrator controller) =>
        {
            await controller.HandleStreamOnlineAsync(channelId, channelName);
            return Results.Ok($"Sent StreamOnlineEvent for {channelName}");
        });

        group.MapPost("/update", async (string channelId, string title, string categoryId, EventBus bus) =>
        {
            await bus.PublishAsync(new ChannelUpdateEvent(channelId, title, categoryId));
            return Results.Ok($"Sent ChannelUpdateEvent: {title} | {categoryId}");
        });
    }
}