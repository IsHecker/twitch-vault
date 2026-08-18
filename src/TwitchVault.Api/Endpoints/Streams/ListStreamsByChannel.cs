using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Streams;

public class ListStreamsByChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels/{channelId}/streams", async (
            string channelId,
            IStreamRepository repo,
            IChannelRepository channelRepo,
            IOptions<PathsOptions> options) =>
        {
            var channel = await channelRepo.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            var streams = await repo.ListByChannelIdAsync(channelId);
            return Results.Ok(streams.Select(s => StreamResponse.FromDomain(s, options.Value.BaseUrl)).ToList());
        })
        .RequireAuthorization()
        .WithName(nameof(ListStreamsByChannel))
        .WithTags("Channels")
        .WithSummary("Get all stream instances for a specific channel")
        .Produces<List<StreamResponse>>()
        .Produces(StatusCodes.Status404NotFound);
}