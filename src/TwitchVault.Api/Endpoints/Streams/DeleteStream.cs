using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
namespace TwitchVault.Api.Endpoints.Streams;

public class DeleteStream : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/streams/{id}", async (
            string id,
            IStreamRepository repo,
            IStreamService streamService,
            RecordingOrchestrator controller) =>
        {
            await streamService.DeleteStreamAsync(id);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(DeleteStream))
        .WithTags("Streams")
        .WithSummary("Delete a finished VOD and its files")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}