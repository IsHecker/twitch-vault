using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Channels;

public class SetArchiveStatus : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/archive", async (
            string channelId,
            Request request,
            AppDbContext db,
            TwitchSubscriptionService twitchSubscription) =>
        {
            var channel = await db.Channels.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            if (channel.IsArchived == request.Archive)
                return Results.BadRequest();

            channel.SetArchivingStatus(request.Archive);
            await db.SaveChangesAsync();

            if (request.Archive)
                _ = twitchSubscription.AddChannelsAsync([channel], default);
            else
                _ = twitchSubscription.RemoveChannelAsync(channel, default);

            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(SetArchiveStatus))
        .WithTags("Channels")
        .WithSummary("[Admin] Enable or disable recording for a channel");

    internal record struct Request(bool Archive);
}