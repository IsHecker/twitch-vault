using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Endpoints.Streams;

public class ToggleStreamDeletion : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/streams/{id}/deletion-mark", async (
            string id,
            Request request,
            IStreamRepository repo,
            IRecordingOrchestrator recordingOrchestrator) =>
        {
            var stream = await repo.GetByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status == StreamStatus.Finished)
                return Results.BadRequest("Cannot mark a finished stream for deletion — delete it directly.");

            await recordingOrchestrator.ToggleStreamDeletionAsync(stream.ChannelId, request.State);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(ToggleStreamDeletion))
        .WithTags("Streams")
        .WithSummary("Mark an active recording for deletion once it finishes")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
    internal readonly record struct Request(bool State);
}