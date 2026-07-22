namespace TwitchVault.Api.Configuration;

public sealed class VaultOptions
{
    public int MaxSegmentDurationInSec { get; set; }
    public int MaxConsecutiveEmptyPolls { get; set; }
}