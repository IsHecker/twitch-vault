using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.Endpoints.Streams;

public class DeleteStreamSegment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/streams/{streamId}/segments/{segmentId}", async (
            string streamId,
            string segmentId,
            StreamRepository repo,
            StreamService streamService,
            StreamController controller) =>
        {
            var stream = await repo.GetStreamByIdAsync(streamId);
            if (stream is null)
                return Results.NotFound();

            if (controller.GetSession(streamId) is not null)
                return Results.BadRequest("Cannot delete a segment while the stream is still recording. Stop it first.");

            await streamService.DeleteSegmentAsync(streamId, segmentId);
            return Results.NoContent();
        })
        .WithName(nameof(DeleteStreamSegment))
        .WithTags("Streams")
        .WithSummary("Delete a finished VOD and its files")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}