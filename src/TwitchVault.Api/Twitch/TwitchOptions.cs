namespace TwitchVault.Api.Twitch;

public sealed class TwitchOptions
{
    public const string SectionName = "Twitch";

    public string PublicClientId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Authorization { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
    public string WebhookPath { get; set; } = string.Empty;
}