namespace TwitchVault.Api.Twitch;

public sealed class TwitchOptions
{
    public const string SectionName = "Twitch";

    public string ClientId { get; set; } = string.Empty;
    public string Authorization { get; set; } = string.Empty;
}