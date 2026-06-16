using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch.EventSub;
using TwitchVault.Api.Events;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Endpoints.Channels;


public class SetRecordingStatus : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/recording", async (
            string channelId,
            Request request,
            ChannelRepository repo,
            TwitchSubscriptionService twitchSubscription) =>
        {
            var channel = await repo.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();
            if (channel.ShouldRecord == request.ShouldRecord)
                return Results.BadRequest();
            channel.ShouldRecord = request.ShouldRecord;
            await repo.UpdateAsync(channel);
            if (request.ShouldRecord)
                _ = twitchSubscription.SubscribeChannelAsync(channel, default);
            else
                _ = twitchSubscription.UnsubscribeChannelAsync(channel, default);
            return Results.NoContent();
        })
        .WithName(nameof(SetRecordingStatus))
        .WithTags("Channels")
        .WithSummary("Enable or disable recording for a channel");
    internal record struct Request(bool ShouldRecord);
}
