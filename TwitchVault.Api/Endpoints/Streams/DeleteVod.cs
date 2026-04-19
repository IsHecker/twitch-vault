using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.Streams;

public class DeleteVod : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/streams/{id}", async (string id, StreamRepository repo) =>
        {
            var stream = await repo.GetByIdAsync(id);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status == StreamStatus.Recording)
                return Results.BadRequest("Cannot delete a stream that is still recording. Stop it first.");

            if (Directory.Exists(stream.FolderPath))
                Directory.Delete(stream.FolderPath, recursive: true);

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