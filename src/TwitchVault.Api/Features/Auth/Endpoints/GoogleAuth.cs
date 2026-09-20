namespace TwitchVault.Api.Features.Auth.Endpoints;

public class GoogleAuth : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/auth/google", async (
            Request request,
            GoogleAuthService authService,
            CancellationToken ct) =>
        {
            var result = await authService.AuthenticateAsync(request.IdToken, ct);
            return result.ToHttpResult();
        })
        .AllowAnonymous()
        .RequireRateLimiting("AuthRateLimit")
        .WithName(nameof(GoogleAuth))
        .WithTags("Auth")
        .WithSummary("Sign in or sign up using a Google ID token")
        .Produces<JwtTokenResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized);

    internal record struct Request(string IdToken);
}