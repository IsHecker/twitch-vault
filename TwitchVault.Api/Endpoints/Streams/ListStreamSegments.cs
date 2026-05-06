using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.Streams;

public class ListStreamSegments : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/streams/{streamId}/segments", async (string streamId, StreamRepository repo) =>
        {
            var stream = await repo.GetStreamByIdAsync(streamId);

            if (stream is null)
                return Results.NotFound();

            var segments = await repo.GetSegmentsByStreamIdAsync(streamId);
            return Results.Ok(segments);
        })
        .WithName(nameof(ListStreamSegments))
        .WithTags("Channels")
        .WithSummary("Get all stream instances for a specific channel")
        .Produces<List<Models.Stream>>()
        .Produces(StatusCodes.Status404NotFound);
}