using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Endpoints.Settings;

public record SettingsResponse(
    TwitchSettingsResponse Twitch
)
{
    public static SettingsResponse FromDomain(AppSettings settings) =>
        new(
            new TwitchSettingsResponse(
                settings.Twitch.Secret,
                settings.Twitch.WebhookPath
            )
        );
}

public record TwitchSettingsResponse(
    string Secret,
    string WebhookPath
);