using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Channels;

public class UpdateChannelQuality : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/quality", async (string channelId, Request request, IChannelRepository repo) =>
        {
            var channel = await repo.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();
            channel.QualityRank = request.QualityRank;
            await repo.UpdateAsync(channel);
            return Results.Ok(ChannelResponse.FromDomain(channel));
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(UpdateChannelQuality))
        .WithTags("Channels")
        .WithSummary("[Admin] Update the quality rank of a channel")
        .Produces<ChannelResponse>();

    internal record struct Request(int QualityRank);
}