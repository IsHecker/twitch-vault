namespace TwitchVault.Api.Endpoints.Admin;

public class GetAllUsers : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/users", async (IUserRepository userRepo) =>
        {
            var users = await userRepo.GetAllAsync();
            var response = users.Select(u => new UserSummary(u.Id, u.Username, u.IsAdmin, u.CreatedAt));
            return Results.Ok(response);
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetAllUsers")
        .WithTags("Admin")
        .WithSummary("[Admin] List all registered users")
        .Produces<List<UserSummary>>();

    internal record UserSummary(Guid Id, string Username, bool IsAdmin, DateTime CreatedAt);
}