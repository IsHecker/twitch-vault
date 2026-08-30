using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Recording;

public interface IChannelService
{
    Task<Result<Channel>> AddChannelAsync(string channelName, int qualityRank, bool shouldRecord, CancellationToken cancellationToken = default);
    Task<Result> DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default);
    Task<Result> SetRecordingStatusAsync(string channelId, bool shouldRecord, CancellationToken cancellationToken = default);
    Task<Result<Channel>> UpdateChannelQualityAsync(string channelId, int qualityRank, CancellationToken cancellationToken = default);
}