namespace TwitchVault.Api.Features.Recording.HLS;

public static class HlsSegmentNaming
{
    public const string SegmentPrefix = "seg_";
    public const string InitPrefix = "init";

    public static string FormatSegmentFileName(int index, string extension) =>
        $"{SegmentPrefix}{index}{extension}";

    public static bool IsSegmentFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var fileName = Path.GetFileName(filePath.AsSpan());
        return fileName.StartsWith(SegmentPrefix, StringComparison.OrdinalIgnoreCase) ||
               fileName.StartsWith(InitPrefix, StringComparison.OrdinalIgnoreCase);
    }

    public static int GetSegmentIndex(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return 0;

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath.AsSpan());
        if (fileNameWithoutExt.StartsWith(InitPrefix, StringComparison.OrdinalIgnoreCase))
            return 0;

        if (!fileNameWithoutExt.StartsWith(SegmentPrefix, StringComparison.OrdinalIgnoreCase) ||
            !int.TryParse(fileNameWithoutExt[SegmentPrefix.Length..], out var parsedIndex))
        {
            return 0;
        }

        return parsedIndex;
    }
}