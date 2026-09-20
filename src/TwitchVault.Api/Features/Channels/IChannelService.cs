namespace TwitchVault.Api.Features.Channels;

public interface IChannelService
{
    Task<Result<Channel>> AddChannelAsync(
        Guid userId,
        string channelName,
        int? qualityRank,
        bool? isArchived,
        bool isAdmin,
        CancellationToken cancellationToken = default);

    Task<Result> UnsubscribeChannelAsync(Guid userId, string channelId, CancellationToken cancellationToken = default);
    Task<Result> DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default);
    Task<Result> SetArchiveStatusAsync(string channelId, bool isArchived, CancellationToken cancellationToken = default);
    Task<Result<Channel>> UpdateChannelQualityAsync(string channelId, int qualityRank, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<ChannelResponse>>> GetChannelsForUserAsync(Guid userId, Pagination pagination, CancellationToken cancellationToken = default);
    Task<PagedResponse<ChannelResponse>> GetAllChannelsAsync(Pagination pagination, CancellationToken cancellationToken = default);
    Task<Result<BannedChannel>> BanChannelAsync(string channelIdentifier, string? reason, CancellationToken cancellationToken = default);
    Task<Result> UnbanChannelAsync(string channelId, CancellationToken cancellationToken = default);
    Task<PagedResponse<BannedChannelResponse>> GetBannedChannelsAsync(Pagination pagination, CancellationToken cancellationToken = default);
}