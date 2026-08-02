using System.Runtime.InteropServices;

namespace TwitchVault.Api.Common;

public class EgyptTimeProvider : IDateTimeProvider
{
    private static readonly TimeZoneInfo EgyptZone =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public DateTime DateTimeNow
        => ToEgyptDateTime(DateTime.UtcNow);

    public static DateTime ToEgyptDateTime(DateTimeOffset dateTime)
    {
        return TimeZoneInfo.ConvertTime(dateTime, EgyptZone).DateTime;
    }
}