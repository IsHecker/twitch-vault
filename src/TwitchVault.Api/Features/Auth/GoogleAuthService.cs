using Google.Apis.Auth;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Endpoints.Auth;
using TwitchVault.Api.Persistence.Extensions;
using static Google.Apis.Auth.GoogleJsonWebSignature;

namespace TwitchVault.Api.Auth;

public sealed class GoogleAuthService(
    AppDbContext db,
    TokenGeneratorService tokenService,
    IOptions<GoogleAuthOptions> googleOptions,
    IDateTimeProvider timeProvider,
    ILogger<GoogleAuthService> logger)
{
    public async Task<Result<JwtTokenResponse>> AuthenticateAsync(string idToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            return Error.Validation("IdToken is required.");

        var clientIdResult = GetClientId();
        if (clientIdResult.IsFailure)
            return clientIdResult.Error;

        var payloadResult = await ValidateGoogleTokenAsync(idToken, clientIdResult.Value);
        if (payloadResult.IsFailure)
            return payloadResult.Error;

        var user = await GetOrCreateUserAsync(payloadResult.Value, ct);
        return tokenService.GenerateToken(user);
    }

    private Result<string> GetClientId()
    {
        var clientId = googleOptions.Value.ClientId;
        if (!string.IsNullOrWhiteSpace(clientId))
            return clientId;

        logger.LogError("Google authentication failed: ClientId is not configured.");
        return Error.Problem("Google ClientId is not configured on the server.");
    }

    private async Task<Result<Payload>> ValidateGoogleTokenAsync(string idToken, string clientId)
    {
        try
        {
            var settings = new ValidationSettings { Audience = [clientId] };
            var payload = await ValidateAsync(idToken, settings);
            return payload;
        }
        catch (InvalidJwtException ex)
        {
            logger.LogWarning(ex, "Google ID token validation failed: {Message}", ex.Message);
            return Error.Unauthorized("Invalid Google ID token.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error validating Google ID token.");
            return Error.Unauthorized("Authentication failed.");
        }
    }

    private async Task<User> GetOrCreateUserAsync(Payload payload, CancellationToken ct)
    {
        var user = await db.Users.GetByGoogleIdAsync(payload.Subject);
        if (user is not null)
            return user;

        user = User.Create(
            id: Guid.NewGuid(),
            username: ResolveUsername(payload),
            email: payload.Email,
            googleId: payload.Subject,
            createdAt: timeProvider.DateTimeNow,
            isAdmin: false);

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    private static string ResolveUsername(Payload payload) =>
        string.IsNullOrWhiteSpace(payload.Name) ? payload.Email.Split('@')[0] : payload.Name;
}