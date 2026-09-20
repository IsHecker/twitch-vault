namespace TwitchVault.Api.Features.Auth.Endpoints;

public record struct JwtTokenResponse(string Token, long ExpiresInSeconds);