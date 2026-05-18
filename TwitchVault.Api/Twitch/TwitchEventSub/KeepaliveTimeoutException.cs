namespace TwitchVault.Api.Twitch.TwitchEventSub;

public sealed class KeepaliveTimeoutException(TimeSpan timeout)
    : Exception($"No message received from Twitch within the keepalive timeout ({timeout.TotalSeconds}s).");