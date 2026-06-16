using TwitchVault.Api.Domain;
using TwitchVault.Api.Recording.HLS;

using TwitchVault.Api.Events;
using TwitchVault.Api.Configuration;
namespace TwitchVault.Api.Endpoints.Settings;


public class GetSettings : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/settings", (SettingsService settings) => Results.Ok(settings.Settings))
            .WithName(nameof(GetSettings))
            .WithTags("Settings")
            .WithSummary("Get current runtime settings")
            .Produces<AppSettings>(StatusCodes.Status200OK);
}
