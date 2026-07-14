namespace TwitchVault.Api.Recording.HLS;

public sealed record PlaylistState(
    DateTime StartTime,
    float TargetDuration,
    float TotalDuration,
    bool HasInitSegment,
    string? LastSegmentFileName,
    long SegmentCount,
    bool LastEntryWasDiscontinuity,
    bool IsFinalized)
{
    public static PlaylistState Empty(DateTime startTime) => new(
        StartTime: startTime,
        TargetDuration: 0,
        TotalDuration: 0,
        HasInitSegment: false,
        LastSegmentFileName: null,
        SegmentCount: 0,
        LastEntryWasDiscontinuity: false,
        IsFinalized: false);
}