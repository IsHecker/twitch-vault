using TwitchVault.Api.Common;

namespace TwitchVault.Api.Models;

public class Channel
{
    public int ChannelId { get; init; } = IdGenerator.Generate();
    public string Name { get; set; } = string.Empty;
    public int QualityRank { get; set; }
    public bool IsLive { get; set; }
    public DateTime? LastStreamedAt { get; set; } = null;
}