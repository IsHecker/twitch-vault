using Microsoft.AspNetCore.Mvc;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Endpoints.Settings;

public class UpdateSettings : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/settings", ([FromBody] RuntimeSettings updated, IConfiguration configuration) =>
        {
            configuration["Vault:MaxSegmentDurationInSec"] = updated.Vault.MaxSegmentDurationInSec.ToString();
            configuration["Vault:MaxConsecutiveEmptyPolls"] = updated.Vault.MaxConsecutiveEmptyPolls.ToString();
            configuration["Vault:UploadBatchSize"] = updated.Vault.UploadBatchSize.ToString();

            configuration["Twitch:PublicClientId"] = updated.Twitch.PublicClientId;
            configuration["Twitch:ClientId"] = updated.Twitch.ClientId;
            configuration["Twitch:Authorization"] = updated.Twitch.Authorization;
            configuration["Twitch:Secret"] = updated.Twitch.Secret;
            configuration["Twitch:WebhookPath"] = updated.Twitch.WebhookPath;

            foreach (var (jobName, jobOpt) in updated.BackgroundJobs)
            {
                configuration[$"BackgroundJobs:{jobName}:Enabled"] = jobOpt.Enabled.ToString();
                configuration[$"BackgroundJobs:{jobName}:RunIntervalInMinutes"] = jobOpt.RunIntervalInMinutes.ToString();
            }

            if (configuration is IConfigurationRoot root)
            {
                root.Reload();
            }

            return Results.NoContent();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(UpdateSettings))
        .WithTags("Settings")
        .WithSummary("Update runtime settings")
        .Accepts<RuntimeSettings>("application/json")
        .Produces(StatusCodes.Status204NoContent);
}