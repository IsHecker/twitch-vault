namespace TwitchVault.Api.CloudStorage.Discord;

public sealed class DiscordOptions
{
    public string ChannelId { get; init; } = null!;
    public string UserToken { get; init; } = null!;
    public string BotToken { get; init; } = null!;
    public string CDNHost { get; init; } = null!;
    public string[] Webhooks { get; init; } = null!;
}