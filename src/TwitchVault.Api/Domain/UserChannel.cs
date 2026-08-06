namespace TwitchVault.Api.Domain;

public sealed record UserChannel(Guid UserId, string ChannelId, DateTime AddedAt);