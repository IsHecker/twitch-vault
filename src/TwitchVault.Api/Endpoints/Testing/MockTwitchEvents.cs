using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Endpoints.Testing;


public class MockTwitchEvents : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/twitch").WithTags("Testing");
        group.MapPost("/online", async (string channelId, string channelName, StreamController controller) =>
        {
            await controller.HandleStreamOnlineAsync(channelId, channelName);
            return Results.Ok($"Sent StreamOnlineEvent for {channelName}");
        });
        group.MapPost("/update", async (string channelId, string title, string category, EventBus bus) =>
        {
            await bus.PublishAsync(new ChannelUpdateEvent(channelId, title, category));
            return Results.Ok($"Sent ChannelUpdateEvent: {title} | {category}");
        });
    }
}
