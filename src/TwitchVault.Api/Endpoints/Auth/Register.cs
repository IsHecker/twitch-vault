using TwitchVault.Api.Auth;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Endpoints.Auth;

public class Register : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/register", async (
            Request request,
            AppDbContext db,
            TokenGeneratorService tokenService,
            IDateTimeProvider timeProvider) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest("Username and password are required.");

            if (request.Password.Length < 8)
                return Results.BadRequest("Password must be at least 8 characters.");

            var existing = await db.Users.GetByUsernameAsync(request.Username);
            if (existing is not null)
                return Results.Conflict("Username is already taken.");

            var user = User.Create(Guid.NewGuid(), request.Username, request.Password, timeProvider.DateTimeNow, isAdmin: false);
            db.Users.Add(user);
            await db.SaveChangesAsync();

            return Results.Ok(tokenService.GenerateToken(user));
        })
        .AllowAnonymous()
        .RequireRateLimiting("AuthRateLimit")
        .WithName(nameof(Register))
        .WithTags("Auth")
        .WithSummary("Register a new user account")
        .Produces<JwtTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

    internal record struct Request(string Username, string Password);
}