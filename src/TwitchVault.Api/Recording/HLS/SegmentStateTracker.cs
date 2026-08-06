using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Recording.HLS;

public sealed class SegmentStateTracker(SettingsService settingsService)
{
    public float AccumulatedDuration { get; private set; }
    public string? CurrentFileName { get; private set; }

    public bool IsFull => AccumulatedDuration >= settingsService.Settings.Vault.MaxSegmentDurationInSec;
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