using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Endpoints.Admin;

public class ListAllChannels : IEndpoint
{
    // TODO: Use pagination
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/channels", async (AppDbContext db) =>
        {
            var channels = await db.Channels
                .AsNoTracking()
                .Select(c => Channels.ChannelResponse.FromDomain(c))
                .ToListAsync();

            return Results.Ok(channels);
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetAllChannels")
        .WithTags("Admin")
        .WithSummary("[Admin] List all channels in the system across all users")
        .Produces<List<Channels.ChannelResponse>>();
}