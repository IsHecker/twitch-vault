using System.Globalization;
using System.Text;

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
        => string.Create(CultureInfo.InvariantCulture, $"{VersionPrefix}:{version}");

    public static string TargetDuration(string seconds)
        => string.Create(CultureInfo.InvariantCulture, $"{TargetDurationPrefix}:{seconds}");

    public static string StartTime(DateTime time)
        => string.Create(CultureInfo.InvariantCulture, $"{StartTimePrefix}:{time:yyyy-MM-ddTHH:mm:ss}");

    public static string MediaSequence(long sequence)
        => string.Create(CultureInfo.InvariantCulture, $"{MediaSequencePrefix}:{sequence}");

    public static string TwitchMediaSequence(string sequence)
        => string.Create(CultureInfo.InvariantCulture, $"{TwitchMediaSequencePrefix}:{sequence}");

    public static string TotalSeconds(string seconds)
        => string.Create(CultureInfo.InvariantCulture, $"{TotalSecondsPrefix}:{seconds}");

    public static string Map(string fileName)
        => string.Create(CultureInfo.InvariantCulture, $"{MapPrefix}=\"{fileName}\"");

    public static string ExtInf(float duration)
        => string.Create(CultureInfo.InvariantCulture, $"{ExtInfPrefix}:{duration:F3},");

    public static class Utf8
    {
        public static readonly byte[] EndList = Encoding.UTF8.GetBytes(HlsTags.EndList);
        public static readonly byte[] MediaSequencePrefix = Encoding.UTF8.GetBytes(HlsTags.MediaSequencePrefix);
        public static readonly byte[] MapPrefix = Encoding.UTF8.GetBytes(HlsTags.MapPrefix);
        public static readonly byte[] ExtInfPrefix = Encoding.UTF8.GetBytes(HlsTags.ExtInfPrefix);
    }
}