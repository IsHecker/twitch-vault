namespace TwitchVault.Api.Domain;

/// <summary>
/// Join entity linking a user to a Twitch channel they are monitoring.
/// Many-to-many: a user can track many channels, a channel can be tracked by many users.
/// </summary>
/// <param name="UserId"></param>
/// <param name="ChannelId"></param>
/// <param name="AddedAt"></param>
public sealed record UserChannel(Guid UserId, string ChannelId, DateTime AddedAt);