using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Endpoints.Channels;

public class ListChannelsByUser : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/{userId:guid}/channels", async (
            Guid userId,
            ClaimsPrincipal principal,
            AppDbContext db) =>
        {
            var userExists = await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId);
            if (!userExists)
                return Results.NotFound($"User '{userId}' not found.");

            var channels = await db.UserChannels
                .AsNoTracking()
                .Include(u => u.Channel)
                .ForUser(userId)
                .Select(u => ChannelResponse.FromDomain(u.Channel))
                .ToListAsync();

            return Results.Ok(channels);
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(ListChannelsByUser))
        .WithTags("Channels")
        .WithSummary("List all channels belonging to a specific user")
        .Produces<List<ChannelResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);
}