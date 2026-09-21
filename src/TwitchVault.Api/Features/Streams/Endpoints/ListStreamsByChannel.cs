using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common.Extensions;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Streams.Endpoints;

public class ListStreamsByChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels/{channelId}/streams", async (
            string channelId,
            ClaimsPrincipal principal,
            [AsParameters] Pagination pagination,
            AppDbContext db,
            IOptions<PathsOptions> options) =>
        {
            var isAdmin = principal.IsInRole("Admin");

            var channel = await db.Channels.AsNoTracking().GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            DateTime? subscribedAt = null;
            if (!isAdmin)
            {
                var isBanned = await db.BannedChannels.AsNoTracking().AnyAsync(b => b.Id == channelId);
                if (isBanned)
                    return Results.NotFound();

                var userId = principal.GetUserId();
                var subscription = await db.Subscriptions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChannelId == channelId);

                if (subscription is null)
                    return Results.NotFound();

                subscribedAt = subscription.AddedAt;
            }

            var query = db.Streams
                .AsNoTracking()
                .ForChannel(channelId)
                .Where(s => s.Status == StreamStatus.Recording
                    || s.StorageLocation == StorageLocation.Remote
                    && s.StorageOperationStatus == StorageOperationStatus.Uploaded);

            if (subscribedAt.HasValue)
                query = query.Where(s => s.StartedAt >= subscribedAt.Value);

            var projected = query.OrderByDescending(s => s.StartedAt)
                .Select(s => StreamResponse.FromDomain(s, options.Value.BaseUrl));

            var paged = await projected.ToPagedResponseAsync(pagination);
            return Results.Ok(paged);
        })
        .RequireAuthorization()
        .WithName(nameof(ListStreamsByChannel))
        .WithTags("Channels")
        .WithSummary("Get all stream instances for a specific channel")
        .Produces<PagedResponse<StreamResponse>>()
        .Produces(StatusCodes.Status404NotFound);
}