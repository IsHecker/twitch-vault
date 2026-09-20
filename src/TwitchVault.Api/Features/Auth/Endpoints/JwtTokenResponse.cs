namespace TwitchVault.Api.Endpoints.Auth;

public record struct JwtTokenResponse(string Token, long ExpiresInSeconds);