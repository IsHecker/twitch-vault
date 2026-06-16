using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Streams;

public class ListStreamsByChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels/{channelId}/streams", async (string channelId, IStreamRepository repo, ChannelRepository channelRepo) =>
        {
            var channel = await channelRepo.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();
            var streams = await repo.GetStreamsByChannelIdAsync(channelId);
            return Results.Ok(streams);
        })
        .WithName(nameof(ListStreamsByChannel))
        .WithTags("Channels")
        .WithSummary("Get all stream instances for a specific channel")
        .Produces<List<Domain.Stream>>()
        .Produces(StatusCodes.Status404NotFound);
}