using TwitchVault.Api.Auth;

namespace TwitchVault.Api.Endpoints.Auth;

public class Login : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/login", async (
            Request request,
            IUserRepository userRepo,
            TokenGeneratorService tokenService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return Results.BadRequest("Username and password are required.");

            var user = await userRepo.GetByUsernameAsync(request.Username);

            if (user is null || request.Password != user.Password)
                return Results.Unauthorized();

            var token = tokenService.GenerateToken(user);
            return Results.Ok(token);
        })
        .AllowAnonymous()
        .WithName(nameof(Login))
        .WithTags("Auth")
        .WithSummary("Login and receive a JWT token")
        .Produces<JwtTokenResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

    internal record struct Request(string Username, string Password);
}