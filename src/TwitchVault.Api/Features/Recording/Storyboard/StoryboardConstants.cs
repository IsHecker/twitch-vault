namespace TwitchVault.Api.Features.Recording.Storyboard;

public static class StoryboardConstants
{
    // Storyboard tiles: 160p
    public const int Cols = 5;
    public const int Rows = 20;
    public const int ThumbWidth = 284;
    public const int ThumbHeight = 160;

    // Strip frames: 360p
    public const int StripFrameCount = 10;
    public const int StripFrameWidth = 640;
    public const int StripFrameHeight = 360;

    public const int JpegQuality = 80;

    public const int TilesPerSheet = Cols * Rows;
    public const int SheetWidth = Cols * ThumbWidth;      // 1420
    public const int SheetHeight = Rows * ThumbHeight;    // 3200
    public const int StripWidth = StripFrameCount * StripFrameWidth;  // 6400
    public const int StripHeight = StripFrameHeight;                  // 360

    public const string ManifestFileName = "storyboard.json";
    public const string StripFileName = "strip.jpg";

    public static string FormatSheetFileName(int sheetIndex) => $"storyboard_{sheetIndex}.jpg";
}