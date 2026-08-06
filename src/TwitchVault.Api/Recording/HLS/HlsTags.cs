using System.Globalization;

namespace TwitchVault.Api.Recording.HLS;

internal static class HlsTags
{
    public const string ExtM3U = "#EXTM3U";
    public const string PlaylistTypeEvent = "#EXT-X-PLAYLIST-TYPE:EVENT";
    public const string EndList = "#EXT-X-ENDLIST";
    public const string Discontinuity = "#EXT-X-DISCONTINUITY";

    public const string VersionPrefix = "#EXT-X-VERSION";
    public const string TargetDurationPrefix = "#EXT-X-TARGETDURATION";
    public const string StartTimePrefix = "#ID3-EQUIV-TDTG";
    public const string MediaSequencePrefix = "#EXT-X-MEDIA-SEQUENCE";
    public const string TwitchMediaSequencePrefix = "#TWITCH-MEDIA-SEQUENCE";
    public const string TotalSecondsPrefix = "#EXT-X-TOTAL-SECS";
    public const string MapPrefix = "#EXT-X-MAP:URI";
    public const string ExtInfPrefix = "#EXTINF";

    public static string Version(int version)
        => $"{VersionPrefix}:{version}";

    public static string TargetDuration(int seconds)
        => $"{TargetDurationPrefix}:{seconds}";

    public static string StartTime(DateTime time)
        => $"{StartTimePrefix}:{time.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}";

    public static string MediaSequence(long sequence)
        => $"{MediaSequencePrefix}:{sequence}";

    public static string TwitchMediaSequence(long sequence)
        => $"{TwitchMediaSequencePrefix}:{sequence}";

    public static string TotalSeconds(float seconds)
        => $"{TotalSecondsPrefix}:{seconds.ToString("F3", CultureInfo.InvariantCulture)}";

    public static string Map(string fileName)
        => $"{MapPrefix}=\"{fileName}\"";

    public static string ExtInf(float duration)
        => $"{ExtInfPrefix}:{duration.ToString("F3", CultureInfo.InvariantCulture)},";
}