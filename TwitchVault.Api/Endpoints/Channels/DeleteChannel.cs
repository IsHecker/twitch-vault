using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.Channels;

public class DeleteChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/channels/{id}", async (int id, ChannelRepository repo, StreamRepository streamRepository) =>
        {
            var channel = await repo.GetByIdAsync(id);

            if (channel is null)
                return Results.NotFound();

            await repo.DeleteAsync(id);
            foreach (var stream in await streamRepository.GetByChannelIdAsync(id))
            {
                await streamRepository.DeleteAsync(stream.TwitchStreamId);
            }
            return Results.NoContent();
        })
        .WithName(nameof(DeleteChannel))
        .WithTags("Channels")
        .WithSummary("Remove a channel from monitoring")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
}