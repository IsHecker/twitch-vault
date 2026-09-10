namespace TwitchVault.Api.Recording.HLS;

public readonly record struct SegmentContent(RemoteSegment Source, Stream Content);