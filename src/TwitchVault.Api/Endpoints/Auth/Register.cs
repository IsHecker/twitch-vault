using TwitchVault.Api.Auth;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Auth;

public class Register : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/register", async (
            Request request,
            IUserRepository userRepo,
            TokenGeneratorService tokenService,
            IDateTimeProvider timeProvider) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest("Username and password are required.");

            if (request.Password.Length < 8)
                return Results.BadRequest("Password must be at least 8 characters.");

            var existing = await userRepo.GetByUsernameAsync(request.Username);
            if (existing is not null)
                return Results.Conflict("Username is already taken.");

            var user = new User
            {
                Id = Guid.NewGuid(),
                Username = request.Username.Trim(),
                Password = request.Password,
                IsAdmin = false,
                CreatedAt = timeProvider.DateTimeNow
            };

            await userRepo.AddAsync(user);

            return Results.Ok(tokenService.GenerateToken(user));
        })
        .AllowAnonymous()
        .WithName(nameof(Register))
        .WithTags("Auth")
        .WithSummary("Register a new user account")
        .Produces<JwtTokenResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict);

    internal record struct Request(string Username, string Password);
}