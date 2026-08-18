using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public record struct RemoteSegment(string Url, float Duration, bool IsInitSegment = false);

public record struct PlaylistExtractionResult(
    IReadOnlyList<RemoteSegment> Segments,
    long LastMediaSequence,
    bool IsStreamEnded);

public static class PlaylistSegmentExtractor
{
    public static PlaylistExtractionResult ExtractNewSegments(string playlistContent, long lastMediaSequence)
    {
        var sequenceStr = HlsTagReader.ReadTagValue(playlistContent, HlsTags.MediaSequencePrefix);
        if (!long.TryParse(sequenceStr, out var firstSequence))
            return new PlaylistExtractionResult([], lastMediaSequence, IsStreamEnded: false);

        var segments = new List<RemoteSegment>();
        var playlistContentSpan = playlistContent.AsSpan();
        var lineRanges = new Range[100];
        var lineCount = playlistContentSpan.Split(lineRanges, '\n');
        long currentSequence = firstSequence - 1;

        for (int i = 0; i < lineCount; i++)
        {
            var line = playlistContentSpan[lineRanges[i]];
            if (line.StartsWith(HlsTags.MapPrefix, StringComparison.Ordinal))
            {
                var initSegmentUrl = HlsTagReader.ReadTagValue(line, HlsTags.MapPrefix).Trim('"');
                segments.Add(new RemoteSegment(initSegmentUrl, 0, IsInitSegment: true));
                continue;
            }

            if (!line.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
                continue;

            if (++currentSequence <= lastMediaSequence)
                continue;

            var duration = float.Parse(HlsTagReader.ReadTagValue(line, HlsTags.ExtInfPrefix, ','));
            if (i + 1 >= lineCount)
                continue;

            segments.Add(new RemoteSegment(playlistContent[lineRanges[++i]].Trim('\r').ToString(), duration));
        }

        var isStreamEnded = playlistContent.AsSpan().Contains(HlsTags.EndList, StringComparison.Ordinal);
        return new PlaylistExtractionResult(segments, currentSequence, isStreamEnded);
    }

    public static PlaylistExtractionResult ExtractAllSegments(string playlistContent)
        => ExtractNewSegments(playlistContent, -1);
}