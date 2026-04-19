using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.Streams;

public class ListStreamsByChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels/{channelId}/streams", async (int channelId, StreamRepository repo, ChannelRepository channelRepo) =>
        {
            var channel = await channelRepo.GetByIdAsync(channelId);

            if (channel is null)
                return Results.NotFound();

            var streams = await repo.GetByChannelIdAsync(channelId);
            return Results.Ok(streams);
        })
        .WithName(nameof(ListStreamsByChannel))
        .WithTags("Channels")
        .WithSummary("Get all stream instances for a specific channel")
        .Produces<List<Models.Stream>>()
        .Produces(StatusCodes.Status404NotFound);
}