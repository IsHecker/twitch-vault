namespace TwitchVault.Api.Features.Channels;

public interface IChannelService
{
    Task<Result<Channel>> SubscribeToChannelAsync(
        string channelName,
        int? qualityRank,
        bool? isArchived,
        CancellationToken cancellationToken = default);

    Task<Result> UnsubscribeChannelAsync(string channelId, CancellationToken cancellationToken = default);

    Task<Result> SetArchiveStatusAsync(string channelId, bool isArchived, CancellationToken cancellationToken = default);

    Task<Result> ChangeChannelQualityAsync(string channelId, int qualityRank, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<ChannelResponse>>> GetChannelsForUserAsync(Guid userId, Pagination pagination, CancellationToken cancellationToken = default);

    Task<PagedResponse<ChannelResponse>> GetAllChannelsAsync(Pagination pagination, CancellationToken cancellationToken = default);
}