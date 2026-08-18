namespace TwitchVault.Api.Recording.HLS;

public sealed record DownloadedSegment(RemoteSegment Source, Stream Content);