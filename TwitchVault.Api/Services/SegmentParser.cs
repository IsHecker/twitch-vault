using TwitchVault.Api.Common;

namespace TwitchVault.Api.Services;

public record struct Segment(string Url, float DurationSeconds, int SequenceNumber = 0)
{
    public readonly bool IsInit => DurationSeconds < 0;
}

public static class SegmentParser
{
    public static IEnumerable<Segment> ParseNewSegments(string mediaPlaylistContent, PlaylistBuilder playlistBuilder)
    {
        var sequenceValue = HlsTagReader.ReadTagValue(mediaPlaylistContent, "#EXT-X-MEDIA-SEQUENCE");
        if (!long.TryParse(sequenceValue, out var currentSequence))
            yield break;

        currentSequence--;

        var manifestMemory = mediaPlaylistContent.AsMemory();
        var lineRanges = new Range[100];
        var lineCount = manifestMemory.Span.Split(lineRanges, '\n');

        for (int i = 0; i < lineCount; i++)
        {
            var line = manifestMemory[lineRanges[i]];

            if (line.Span.StartsWith("#EXT-X-MAP:URI", StringComparison.Ordinal))
            {
                var initUri = HlsTagReader.ReadTagValue(line.Span, "#EXT-X-MAP:URI");
                yield return new Segment(initUri.Trim('"'), -1);
            }

            if (!line.Span.StartsWith("#EXTINF", StringComparison.Ordinal))
                continue;

            if (++currentSequence <= playlistBuilder.TwitchMediaSequence)
                continue;

            var duration = float.Parse(HlsTagReader.ReadTagValue(line.Span, "#EXTINF", ','));
            if (i + 1 < lineCount)
                yield return new Segment(mediaPlaylistContent[lineRanges[++i]].Trim('\r').ToString(), duration);
        }

        playlistBuilder.SetTwitchMediaSequence(currentSequence);
    }
}