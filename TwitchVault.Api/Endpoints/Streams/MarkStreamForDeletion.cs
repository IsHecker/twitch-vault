using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.Endpoints.Streams;

public class ToggleStreamDeletion : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/streams/{id}/deletion-mark/{state:bool}", async (string id, bool state, StreamRepository repo, StreamController controller) =>
        {
            var stream = await repo.GetByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status == StreamStatus.Finished)
                return Results.BadRequest("Cannot mark a finished stream for deletion — delete it directly.");

            await controller.ToggleStreamDeletionAsync(id, state);
            return Results.NoContent();
        })
        .WithName(nameof(ToggleStreamDeletion))
        .WithTags("Streams")
        .WithSummary("Mark an active recording for deletion once it finishes")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}