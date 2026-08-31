using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Endpoints.Channels;

public record ChannelResponse(
    string Id,
    string Name,
    int QualityRank,
    bool IsLive,
    bool IsArchived,
    DateTime? LastStreamedAt
)
{
    public static ChannelResponse FromDomain(Channel channel) =>
        new(
            channel.Id,
            channel.Name,
            channel.QualityRank,
            channel.IsLive,
            channel.IsArchived,
            channel.LastStreamedAt
        );
}