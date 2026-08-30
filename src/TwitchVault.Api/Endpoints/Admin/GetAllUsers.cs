using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Endpoints.Admin;

public class GetAllUsers : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/users", async (AppDbContext db) =>
        {
            var users = await db.Users
                .AsNoTracking()
                .Select(u => new UserSummary(u.Id, u.Username, u.IsAdmin, u.CreatedAt))
                .ToListAsync();

            return Results.Ok(users);
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetAllUsers")
        .WithTags("Admin")
        .WithSummary("[Admin] List all registered users")
        .Produces<List<UserSummary>>();

    internal record UserSummary(Guid Id, string Username, bool IsAdmin, DateTime CreatedAt);
}