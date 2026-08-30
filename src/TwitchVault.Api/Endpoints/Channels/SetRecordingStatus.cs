using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Channels;

public class SetRecordingStatus : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/recording", async (
            string channelId,
            Request request,
            AppDbContext db,
            TwitchSubscriptionService twitchSubscription) =>
        {
            var channel = await db.Channels.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            if (channel.IsArchived == request.ShouldRecord)
                return Results.BadRequest();

            channel.SetArchivingStatus(request.ShouldRecord);
            await db.SaveChangesAsync();

            if (request.ShouldRecord)
                _ = twitchSubscription.AddChannelsAsync([channel], default);
            else
                _ = twitchSubscription.RemoveChannelAsync(channel, default);

            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(SetRecordingStatus))
        .WithTags("Channels")
        .WithSummary("[Admin] Enable or disable recording for a channel");

    internal record struct Request(bool ShouldRecord);
}