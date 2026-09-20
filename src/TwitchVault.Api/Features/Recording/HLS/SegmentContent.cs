
namespace TwitchVault.Api.Features.Recording.HLS;

public readonly record struct SegmentContent(RemoteSegment Source, ResponseStream ResponseStream);