namespace TwitchVault.Api.Features.Recording.Storyboard;

public sealed class StoryboardCompilationProgress
{
    public const string ProgressFileName = "storyboard_progress.json";

    public int LastProcessedSegmentIndex { get; set; } = -1;
    public int AccumulatedSeconds { get; set; }
    public int NextCaptureSeconds { get; set; }
    public int TotalFrames { get; set; }
    public int CurrentSheetIndex { get; set; }
    public int TilesOnCurrentSheet { get; set; }
    public int StripFramesCaptured { get; set; }
    public List<int> ExtractedStripSegmentIndices { get; set; } = [];

    public static StoryboardCompilationProgress Create() => new()
    {
        LastProcessedSegmentIndex = -1,
        AccumulatedSeconds = 0,
        NextCaptureSeconds = 0, // capture at start
        TotalFrames = 0,
        CurrentSheetIndex = 0,
        TilesOnCurrentSheet = 0,
        StripFramesCaptured = 0,
        ExtractedStripSegmentIndices = []
    };
}