namespace TwitchVault.Api.CloudStorage.Discord;

public sealed class DiscordOptions
{
    public const string SectionName = "Discord";

    public string ChannelId { get; init; } = null!;
    public string UserToken { get; init; } = null!;
    public string BotToken { get; init; } = null!;
    public string CDNHost { get; init; } = null!;
    public int LocalRetentionDays { get; init; }
    public int MaxAttachmentsPerMessage { get; init; }
    public int MessageSizeLimitInMB { get; init; }
    public string[] Webhooks { get; init; } = null!;
}