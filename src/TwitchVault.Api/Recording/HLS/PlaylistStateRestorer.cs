using TwitchVault.Api.Common;

namespace TwitchVault.Api.Recording.HLS;

public static class PlaylistStateRestorer
{
    public static PlaylistState Restore(IReadOnlyList<string> lines)
    {
        DateTime? startTime = null;
        float targetDuration = 0;
        float totalDuration = 0;
        bool hasInitSegment = false;
        string? lastSegmentFileName = null;
        long segmentCount = 0;
        bool isFinalized = false;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (line.StartsWith(HlsTags.TwitchMediaSequencePrefix))
            {
                var shit = HlsTagReader.ReadTagValue(line, HlsTags.TwitchMediaSequencePrefix);
                segmentCount = long.Parse(shit);
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
            segmentCount,
            isFinalized);
    }
}