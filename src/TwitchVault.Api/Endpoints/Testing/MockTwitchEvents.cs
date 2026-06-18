using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Endpoints.Testing;

public class MockTwitchEvents : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/twitch").WithTags("Testing");
        group.MapPost("/online", async (
            string channelId,
            string channelName,
            StreamController controller,
            ITwitchGqlClient twitchGqlClient) =>
        {
            var metadata = await twitchGqlClient.GetStreamMetadataAsync(channelName, default);

            await controller.HandleStreamOnlineAsync(channelId, channelName, metadata.Value);
            return Results.Ok($"Sent StreamOnlineEvent for {channelName}");
        });

        group.MapPost("/update", async (string channelId, string title, string category, EventBus bus) =>
        {
            await bus.PublishAsync(new ChannelUpdateEvent(channelId, title, category));
            return Results.Ok($"Sent ChannelUpdateEvent: {title} | {category}");
        });
    }
}