using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording.HLS;

public readonly record struct SegmentContent(RemoteSegment Source, ResponseStream ResponseStream);