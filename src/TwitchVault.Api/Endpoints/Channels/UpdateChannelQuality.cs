using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Endpoints.Channels;

public class UpdateChannelQuality : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/quality", async (
            string channelId,
            Request request,
            AppDbContext db) =>
        {
            var channel = await db.Channels.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            channel.UpdateQualityRank(request.QualityRank);
            await db.SaveChangesAsync();
            return Results.Ok(ChannelResponse.FromDomain(channel));
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(UpdateChannelQuality))
        .WithTags("Channels")
        .WithSummary("[Admin] Update the quality rank of a channel")
        .Produces<ChannelResponse>();

    internal record struct Request(int QualityRank);
}