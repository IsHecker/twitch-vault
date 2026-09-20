namespace TwitchVault.Api.Features.Recording.HLS;

public static class PlaylistStateRestorer
{
    public static PlaylistState Restore(IReadOnlyList<string> lines)
    {
        DateTime? startTime = null;
        float targetDuration = 0;
        float totalDuration = 0;
        bool hasInitSegment = false;
        string? lastSegmentFileName = null;
        long twitchSegmentCount = 0;
        bool isFinalized = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.StartsWith(HlsTags.TwitchMediaSequencePrefix))
            {
                twitchSegmentCount = long.Parse(HlsTagReader.ReadTagValue(line, HlsTags.TwitchMediaSequencePrefix));
                continue;
            }
            else if (line.StartsWith(HlsTags.TargetDurationPrefix))
            {
                _ = float.TryParse(HlsTagReader.ReadTagValue(line, HlsTags.TargetDurationPrefix), out targetDuration);
            }
            else if (line.StartsWith(HlsTags.TotalSecondsPrefix))
            {
                _ = float.TryParse(HlsTagReader.ReadTagValue(line, HlsTags.TotalSecondsPrefix), out totalDuration);
            }
            else if (line.StartsWith(HlsTags.StartTimePrefix))
            {
                if (DateTime.TryParse(HlsTagReader.ReadTagValue(line, HlsTags.StartTimePrefix), out var parsed))
                    startTime = parsed;
            }
            else if (line.StartsWith(HlsTags.MapPrefix))
            {
                hasInitSegment = true;
            }
            else if (line.Equals(HlsTags.EndList))
            {
                isFinalized = true;
            }
            else if (line.StartsWith(HlsTags.ExtInfPrefix))
            {
                if (i + 1 >= lines.Count)
                    continue;

                lastSegmentFileName = lines[++i];
            }
        }

        return new PlaylistState(
            startTime ?? DateTime.Now,
            targetDuration,
            totalDuration,
            hasInitSegment,
            lastSegmentFileName,
            twitchSegmentCount,
            lines[^1].Equals(HlsTags.Discontinuity),
            isFinalized);
    }
}