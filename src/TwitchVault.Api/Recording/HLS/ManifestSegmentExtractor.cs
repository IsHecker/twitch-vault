using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public record struct HlsSegment(string Url, float Duration);

public record struct ManifestExtractionResult(
    IReadOnlyList<HlsSegment> Segments,
    string? InitSegmentUrl,
    long LastMediaSequence,
    bool IsStreamEnded);

public static class ManifestSegmentExtractor
{
    public static ManifestExtractionResult ExtractNewSegments(string manifestContent, long lastMediaSequence)
    {
        var sequenceStr = HlsTagReader.ReadTagValue(manifestContent, "#EXT-X-MEDIA-SEQUENCE");
        if (!long.TryParse(sequenceStr, out var firstSequence))
            return new ManifestExtractionResult([], null, lastMediaSequence, IsStreamEnded: false);

        var segments = new List<HlsSegment>();
        string? initSegmentUrl = null;

        var manifestSpan = manifestContent.AsSpan();
        var lineRanges = new Range[100];
        var lineCount = manifestSpan.Split(lineRanges, '\n');
        long currentSequence = firstSequence - 1;

        for (int i = 0; i < lineCount; i++)
        {
            var line = manifestSpan[lineRanges[i]];
            if (line.StartsWith("#EXT-X-MAP:URI", StringComparison.Ordinal))
            {
                initSegmentUrl = HlsTagReader.ReadTagValue(line, "#EXT-X-MAP:URI").Trim('"');
                continue;
            }

            if (!line.StartsWith("#EXTINF", StringComparison.Ordinal))
                continue;

            if (++currentSequence <= lastMediaSequence)
                continue;

            var duration = float.Parse(HlsTagReader.ReadTagValue(line, "#EXTINF", ','));
            if (i + 1 >= lineCount)
                continue;

            segments.Add(new HlsSegment(manifestContent[lineRanges[++i]].Trim('\r').ToString(), duration));
        }

        var isStreamEnded = manifestContent.AsSpan().Contains("#EXT-X-ENDLIST", StringComparison.Ordinal);
        return new ManifestExtractionResult(segments, initSegmentUrl, currentSequence, isStreamEnded);
    }

    public static ManifestExtractionResult ExtractAllSegments(string manifestContent)
        => ExtractNewSegments(manifestContent, -1);
}