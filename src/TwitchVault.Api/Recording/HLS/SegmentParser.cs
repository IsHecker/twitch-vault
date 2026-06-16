using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public record struct HlsSegment(string Url, float DurationSeconds);

public record struct PlaylistResult(
    IReadOnlyList<HlsSegment> Segments,
    string? InitSegmentUrl,        // null when not present or already handled
    long LastMediaSequence,
    bool IsStreamEnded);

public static class SegmentParser
{
    public static PlaylistResult ParseNewSegments(string manifestContent, long lastKnownSequence)
    {
        var sequenceStr = HlsTagReader.ReadTagValue(manifestContent, "#EXT-X-MEDIA-SEQUENCE");
        if (!long.TryParse(sequenceStr, out var firstSequence))
            return new PlaylistResult([], null, lastKnownSequence, IsStreamEnded: false);

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

            if (++currentSequence <= lastKnownSequence)
                continue;

            var duration = float.Parse(HlsTagReader.ReadTagValue(line, "#EXTINF", ','));
            if (i + 1 >= lineCount)
                continue;

            segments.Add(new HlsSegment(manifestContent[lineRanges[++i]].Trim('\r').ToString(), duration));
        }

        // var isStreamEnded = manifestSpan[lineRanges[^1]].Contains("#EXT-X-ENDLIST", StringComparison.Ordinal);
        var isStreamEnded = manifestContent.AsSpan().Contains("#EXT-X-ENDLIST", StringComparison.Ordinal);
        return new PlaylistResult(segments, initSegmentUrl, currentSequence, isStreamEnded);
    }
}