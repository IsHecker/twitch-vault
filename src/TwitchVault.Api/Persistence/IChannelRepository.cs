using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence;

public interface IChannelRepository
{
    Task<List<Channel>> GetAllAsync();
    Task<Channel?> GetByIdAsync(string channelId);
    Task<Channel?> GetByNameAsync(string name);
    Task AddAsync(Channel channel);
    Task UpdateAsync(Channel channel);
    Task DeleteAsync(string channelId);
    Task SetLiveAsync(string channelId, bool isLive);
    Task UpdateLastStreamedAtAsync(string channelId, DateTime lastStreamedAt);
}