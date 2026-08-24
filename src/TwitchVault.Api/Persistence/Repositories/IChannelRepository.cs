using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Repositories;

public interface IChannelRepository
{
    Task<IEnumerable<Channel>> GetAllAsync();
    Task<IEnumerable<Channel>> GetByIdsAsync(IEnumerable<string> ids);
    Task<Channel?> GetByIdAsync(string channelId);
    Task<Channel?> GetByNameAsync(string name);
    Task AddAsync(Channel channel);
    Task UpdateAsync(Channel channel);
    Task DeleteAsync(string channelId);
    Task SetLiveAsync(string channelId, bool isLive);
    Task UpdateLastStreamedAtAsync(string channelId, DateTime lastStreamedAt);
}