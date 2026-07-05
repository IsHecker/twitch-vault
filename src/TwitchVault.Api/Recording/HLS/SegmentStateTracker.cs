using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Recording.HLS;

public sealed class SegmentStateTracker(SettingsService settingsService)
{
    private const string SegmentPrefix = "seg_";

    public float AccumulatedDuration { get; private set; }
    public string? CurrentFileName { get; private set; }

    public bool IsFull => AccumulatedDuration >= settingsService.Settings.Vault.MaxSegmentDurationInSec;
    public bool HasOpenSegment => CurrentFileName is not null;

    public string GetOrStartSegment(string? lastFlushedFileName, string urlExtension) =>
        CurrentFileName ?? StartNewSegment(lastFlushedFileName, urlExtension);

    private string StartNewSegment(string? lastFlushedFileName, string urlExtension)
    {
        var nextIndex = GetNextSegmentNumber(lastFlushedFileName);
        CurrentFileName = $"{SegmentPrefix}{nextIndex}{urlExtension}";
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

    private static int GetNextSegmentNumber(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return 1;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        return int.Parse(nameWithoutExtension[SegmentPrefix.Length..]) + 1;
    }
}