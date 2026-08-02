using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Endpoints.Settings;

public class GetSettings : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/settings", (SettingsService settings) => Results.Ok(SettingsResponse.FromDomain(settings.Settings)))
            .WithName(nameof(GetSettings))
            .WithTags("Settings")
            .WithSummary("Get current runtime settings")
            .Produces<SettingsResponse>(StatusCodes.Status200OK);
}