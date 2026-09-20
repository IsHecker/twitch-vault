using TwitchVault.Api.Configuration;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Features.Recording.HLS;

public sealed class SegmentStateTracker(IOptionsMonitor<VaultOptions> vaultOptions)
{
    public float AccumulatedDuration { get; private set; }
    public string? CurrentFileName { get; private set; }

    public bool IsFull => AccumulatedDuration >= vaultOptions.CurrentValue.MaxSegmentDurationInSec;
    public bool HasOpenSegment => CurrentFileName is not null;

    public string GetOrStartSegment(string? lastFlushedFileName, string urlExtension) =>
        CurrentFileName ?? StartNewSegment(lastFlushedFileName, urlExtension);

    private string StartNewSegment(string? lastFlushedFileName, string urlExtension)
    {
        var nextIndex = HlsSegmentNaming.GetSegmentIndex(lastFlushedFileName) + 1;
        CurrentFileName = HlsSegmentNaming.FormatSegmentFileName(nextIndex, urlExtension);
        AccumulatedDuration = 0f;
        return CurrentFileName;
    }

    public void AddDuration(float seconds) => AccumulatedDuration += seconds;

    public (string FileName, float Duration) CloseSegment()
    {
        var result = (CurrentFileName!, AccumulatedDuration);
        CurrentFileName = null;
        AccumulatedDuration = 0f;
        return result;
    }
}