using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Common.Extensions;

namespace TwitchVault.Api.Features.Auth.Endpoints;

public class ListAllUsers : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/users", async (
            [AsParameters] Pagination pagination,
            AppDbContext db) =>
        {
            var query = db.Users
                .AsNoTracking()
                .OrderBy(u => u.Username)
                .Select(u => new UserSummary(u.Id, u.Username, u.IsAdmin, u.CreatedAt));

            var paged = await query.ToPagedResponseAsync(pagination);
            return Results.Ok(paged);
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetAllUsers")
        .WithTags("Admin")
        .WithSummary("[Admin] List all registered users")
        .Produces<PagedResponse<UserSummary>>();

    internal record UserSummary(Guid Id, string Username, bool IsAdmin, DateTime CreatedAt);
}