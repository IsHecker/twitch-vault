using TwitchVault.Api.Common;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.Endpoints.Streams;

public class DeleteVod : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/streams/{id}", async (string id, StreamRepository repo, StreamController controller) =>
        {
            var stream = await repo.GetByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status == StreamStatus.Recording || controller.GetSession(id) is not null)
                return Results.BadRequest("Cannot delete a stream that is still recording or finishing. Stop it first.");

            await IOUtils.DeleteDirectoryWithRetriesAsync(stream.FolderPath);

            await repo.DeleteAsync(id);
            return Results.NoContent();
        })
        .WithName(nameof(DeleteVod))
        .WithTags("Streams")
        .WithSummary("Delete a finished VOD and its files")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status400BadRequest);
}