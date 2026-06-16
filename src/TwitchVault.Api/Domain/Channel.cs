namespace TwitchVault.Api.Domain;

public class Channel
{
    public string ChannelId { get; init; } = null!;
    public string Name { get; set; } = string.Empty;
    public int QualityRank { get; set; }
    public bool IsLive { get; set; }
    public bool ShouldRecord { get; set; }
    public DateTime? LastStreamedAt { get; set; } = null;
}
