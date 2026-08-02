using Microsoft.AspNetCore.Mvc;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Endpoints.Settings;

public class UpdateSettings : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/settings", async ([FromBody] AppSettings updated, SettingsService settings) =>
        {
            await settings.UpdateAsync(updated);
            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(UpdateSettings))
        .WithTags("Settings")
        .WithSummary("Update runtime settings")
        .Accepts<AppSettings>("application/json")
        .Produces(StatusCodes.Status204NoContent);
}
