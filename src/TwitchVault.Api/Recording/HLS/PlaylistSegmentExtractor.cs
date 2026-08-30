using System.Globalization;
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
        var lineRanges = new Range[playlistContentSpan.Count('\n') + 1];
        var lineCount = playlistContentSpan.Split(lineRanges, '\n');
        long currentSequence = firstSequence - 1;
        var isStreamEnded = false;

        for (int i = 0; i < lineCount; i++)
        {
            var line = playlistContentSpan[lineRanges[i]];
            if (line.StartsWith(HlsTags.EndList, StringComparison.Ordinal))
            {
                isStreamEnded = true;
                continue;
            }

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

        return new PlaylistExtractionResult(segments, currentSequence, isStreamEnded);
    }

    public static IEnumerable<RemoteSegment> EnumerateSegments(IEnumerable<string> lines)
    {
        float? pendingDuration = null;
        foreach (var line in lines)
        {
            var trimmed = line.AsSpan().Trim();
            if (trimmed.IsEmpty)
                continue;

            if (trimmed.StartsWith(HlsTags.MapPrefix, StringComparison.Ordinal))
            {
                var initUrl = HlsTagReader.ReadTagValue(trimmed, HlsTags.MapPrefix).Trim('"');
                yield return new RemoteSegment(initUrl, 0, IsInitSegment: true);
                continue;
            }

            if (trimmed.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
            {
                var durationStr = HlsTagReader.ReadTagValue(trimmed, HlsTags.ExtInfPrefix, ',');
                pendingDuration = float.Parse(durationStr, CultureInfo.InvariantCulture);
                continue;
            }

            if (pendingDuration is not null)
            {
                yield return new RemoteSegment(trimmed.ToString(), pendingDuration.Value);
                pendingDuration = null;
            }
        }
    }
}