namespace TwitchVault.Api.Recording.HLS;

public sealed record SegmentContent(RemoteSegment Source, Stream Content);