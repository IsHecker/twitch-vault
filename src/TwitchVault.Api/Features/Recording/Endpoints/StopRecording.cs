using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Recording.Endpoints;

public class StopRecording : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/streams/{id}/stop", async (
            string id,
            AppDbContext db,
            IRecordingOrchestrator recordingOrchestrator) =>
        {
            var stream = await db.Streams.AsNoTracking().GetByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status != StreamStatus.Recording)
                return Results.BadRequest("Stream is not currently recording.");

            if (stream.ChannelId is not null)
                await recordingOrchestrator.StopRecordingAsync(stream.ChannelId);
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