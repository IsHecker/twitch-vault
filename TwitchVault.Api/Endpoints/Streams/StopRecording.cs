using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.Endpoints.Streams;

public class StopRecording : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/streams/{id}/stop", async (string id, StreamRepository repo, StreamController controller) =>
        {
            var stream = await repo.GetStreamByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status != StreamStatus.Recording)
                return Results.BadRequest("Stream is not currently recording.");

            await controller.StopRecordingAsync(id);
            return Results.NoContent();
        })
        .WithName(nameof(StopRecording))
        .WithTags("Streams")
        .WithSummary("Immediately stop an active recording")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}