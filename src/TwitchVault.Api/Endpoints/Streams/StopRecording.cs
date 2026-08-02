using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
namespace TwitchVault.Api.Endpoints.Streams;

public class StopRecording : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/streams/{id}/stop", async (string id, IStreamRepository repo, RecordingOrchestrator controller) =>
        {
            var stream = await repo.GetByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status != StreamStatus.Recording)
                return Results.BadRequest("Stream is not currently recording.");

            await controller.StopRecordingAsync(stream.ChannelId);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(StopRecording))
        .WithTags("Streams")
        .WithSummary("Immediately stop an active recording")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}