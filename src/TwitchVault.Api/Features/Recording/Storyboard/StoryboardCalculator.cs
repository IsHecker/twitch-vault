namespace TwitchVault.Api.Features.Recording.Storyboard;

public static class StoryboardCalculator
{
    public const double DefaultDensityModifier = 1.0;
    public const double DefaultGrowthStrength = 0.25;
    public const int MinIntervalSeconds = 2;

    /// <summary>
    /// Calculates the dynamic frame capture interval in seconds for storyboard generation:
    /// Interval = D / (200 * M * (1 + G * ln(1 + H)))
    /// Clamped to a minimum of 2.0 seconds.
    /// </summary>
    public static int CalculateStoryboardInterval(
        double durationSeconds,
        double densityModifier = DefaultDensityModifier,
        double growthStrength = DefaultGrowthStrength)
    {
        if (durationSeconds <= 0)
            return 0;

        var durationHours = durationSeconds / 3600.0;
        var denominator = 200.0 * densityModifier * (1.0 + (growthStrength * Math.Log(1.0 + durationHours)));

        var rawInterval = denominator <= 0
            ? MinIntervalSeconds
            : durationSeconds / denominator;

        // Snap to a multiple of the keyframe interval (2 s) so every tile timestamp
        // lands exactly on a keyframe boundary, keeping seek-click and tile in sync.
        var interval = (int)Math.Round(Math.Max(MinIntervalSeconds, rawInterval));
        return interval % 2 == 0 ? interval : interval + 1;
    }

    public static List<int> CalculateStripTimestamps(
        double totalDurationSeconds,
        int targetFrameCount = StoryboardConstants.StripFrameCount)
    {
        if (totalDurationSeconds <= 0 || targetFrameCount <= 0)
            return [];

        var count = Math.Min(targetFrameCount, (int)Math.Max(1, Math.Floor(totalDurationSeconds)));
        var interval = totalDurationSeconds / count;

        var timestamps = new List<int>(count);
        for (var i = 0; i < count; i++)
        {
            var raw = (i + 0.5) * interval;
            timestamps.Add((int)Math.Floor(raw / 2.0) * 2);
        }

        return timestamps;
    }

    /// <summary>
    /// Estimates the total segment count N by dividing total duration by 10 seconds.
    /// </summary>
    public static int EstimateSegmentCount(double totalDurationSeconds)
    {
        if (totalDurationSeconds <= 0)
            return 0;

        return Math.Max(1, (int)Math.Round(totalDurationSeconds / 10.0));
    }

    /// <summary>
    /// Computes the target 0-based segment indices for extracting strip frames.
    /// Standard (N >= 10): 1 + Round(i * (N - 3) / 9) for i in [0, 9]
    /// Short (3 < N < 10): 1 + Round(i * (N - 3) / (K - 1)) for i in [0, K - 1], where K = N - 2
    /// Ultra-short (N <= 3): [0..N - 1]
    /// </summary>
    public static List<int> CalculateStripSegmentIndices(int totalSegments)
    {
        if (totalSegments <= 0)
            return [];

        var indices = new HashSet<int>();

        if (totalSegments <= 3)
        {
            for (var i = 0; i < totalSegments; i++)
            {
                indices.Add(i);
            }
        }
        else if (totalSegments < 10)
        {
            var k = totalSegments - 2;
            for (var i = 0; i < k; i++)
            {
                var idx = 1 + (int)Math.Round(i * (double)(totalSegments - 3) / (k - 1));
                indices.Add(Math.Clamp(idx, 0, totalSegments - 1));
            }
        }
        else
        {
            for (var i = 0; i < 10; i++)
            {
                var idx = 1 + (int)Math.Round(i * (double)(totalSegments - 3) / 9.0);
                indices.Add(Math.Clamp(idx, 0, totalSegments - 1));
            }
        }

        return indices.OrderBy(x => x).ToList();
    }
}