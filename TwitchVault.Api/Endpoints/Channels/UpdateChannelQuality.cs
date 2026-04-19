using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.Channels;

public class UpdateChannelQuality : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId:int}/quality", async (int channelId, Request request, ChannelRepository repo) =>
        {
            var channel = await repo.GetByIdAsync(channelId);
            if (channel is null) 
                return Results.NotFound();

            channel.QualityRank = request.QualityRank;
            await repo.UpdateAsync(channel);

            return Results.Ok(channel);
        })
        .WithName(nameof(UpdateChannelQuality))
        .WithTags("Channels")
        .WithSummary("Update the quality rank of a channel");

    internal record struct Request(int QualityRank);
}