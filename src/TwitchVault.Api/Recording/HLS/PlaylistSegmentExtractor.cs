using System.Globalization;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public readonly record struct RemoteSegment(string Url, float Duration, bool IsInitSegment = false);

public record struct PlaylistExtractionResult(
    IReadOnlyList<RemoteSegment> Segments,
    long LastMediaSequence,
    bool IsStreamEnded);

public static class PlaylistSegmentExtractor
{
    public static PlaylistExtractionResult ExtractNewSegments(string playlistContent, long lastMediaSequence)
    {
        var playlistContentSpan = playlistContent.AsSpan();
        var sequenceSpan = HlsTagReader.ReadTagValueSpan(playlistContentSpan, HlsTags.MediaSequencePrefix);
        if (!long.TryParse(sequenceSpan, out var firstSequence))
            return new PlaylistExtractionResult([], lastMediaSequence, IsStreamEnded: false);

        List<RemoteSegment>? segments = null;
        long currentSequence = firstSequence - 1;
        var isStreamEnded = false;

        var lineEnumerator = playlistContentSpan.EnumerateLines();
        while (lineEnumerator.MoveNext())
        {
            segments ??= [];

            var line = lineEnumerator.Current;
            if (line.StartsWith(HlsTags.EndList, StringComparison.Ordinal))
            {
                isStreamEnded = true;
                continue;
            }

            if (line.StartsWith(HlsTags.MapPrefix, StringComparison.Ordinal))
            {
                var initSegmentUrl = HlsTagReader.ReadTagValueSpan(line, HlsTags.MapPrefix).Trim('"').ToString();
                segments.Add(new RemoteSegment(initSegmentUrl, 0, IsInitSegment: true));
                continue;
            }

            if (!line.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
                continue;

            if (++currentSequence <= lastMediaSequence)
                continue;

            if (!lineEnumerator.MoveNext())
                continue;

            var durationSpan = HlsTagReader.ReadTagValueSpan(line, HlsTags.ExtInfPrefix, ',');
            var duration = float.Parse(durationSpan, CultureInfo.InvariantCulture);
            segments.Add(new RemoteSegment(lineEnumerator.Current.ToString(), duration));
        }

        return new PlaylistExtractionResult(segments ?? (IReadOnlyList<RemoteSegment>)[], currentSequence, isStreamEnded);
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
                var initUrl = HlsTagReader.ReadTagValueSpan(trimmed, HlsTags.MapPrefix).Trim('"').ToString();
                yield return new RemoteSegment(initUrl, 0, IsInitSegment: true);
                continue;
            }

            if (trimmed.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
            {
                var durationSpan = HlsTagReader.ReadTagValueSpan(trimmed, HlsTags.ExtInfPrefix, ',');
                pendingDuration = float.Parse(durationSpan, CultureInfo.InvariantCulture);
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