using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Features.Settings.Endpoints;

public class GetSettings : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/settings", (IOptionsMonitor<RuntimeSettings> settings) => Results.Ok(settings.CurrentValue))
            .RequireAuthorization()
            .WithName(nameof(GetSettings))
            .WithTags("Settings")
            .WithSummary("Get current runtime settings")
            .Produces<RuntimeSettings>(StatusCodes.Status200OK);
}