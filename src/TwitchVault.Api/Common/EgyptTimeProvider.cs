using System.Runtime.InteropServices;

namespace TwitchVault.Api.Common;

public class EgyptTimeProvider : IDateTimeProvider
{
    private readonly TimeZoneInfo EgyptZone =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time")
            : TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public DateTime DateTimeNow
        => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, EgyptZone);
}