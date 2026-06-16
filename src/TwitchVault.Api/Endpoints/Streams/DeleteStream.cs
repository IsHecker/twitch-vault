using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
namespace TwitchVault.Api.Endpoints.Streams;

public class DeleteStream : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/streams/{id}", async (
            string id,
            IStreamRepository repo,
            StreamService streamService,
            StreamController controller) =>
        {
            var stream = await repo.GetStreamByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status == StreamStatus.Recording || controller.GetSession(id) is not null)
                return Results.BadRequest("Cannot delete a stream that is still recording or finishing. Stop it first.");

            await streamService.DeleteStreamAsync(id);
            return Results.NoContent();
        })
        .WithName(nameof(DeleteStream))
        .WithTags("Streams")
        .WithSummary("Delete a finished VOD and its files")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}