using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Endpoints.Streams;

public class ListStreamsByChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels/{channelId}/streams", async (
            string channelId,
            AppDbContext db,
            IOptions<PathsOptions> options) =>
        {
            var channel = await db.Channels.AsNoTracking().GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            var activeStreams = await db.Streams
                .AsNoTracking()
                .ForChannel(channelId)
                .Where(s => s.StorageLocation == StorageLocation.Remote
                    && s.StorageOperationStatus == StorageOperationStatus.Uploaded)
                .Select(s => StreamResponse.FromDomain(s, options.Value.BaseUrl))
                .ToListAsync();

            return Results.Ok(activeStreams);
        })
        .RequireAuthorization()
        .WithName(nameof(ListStreamsByChannel))
        .WithTags("Channels")
        .WithSummary("Get all stream instances for a specific channel")
        .Produces<List<StreamResponse>>()
        .Produces(StatusCodes.Status404NotFound);
}