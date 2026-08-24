using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Repositories;

public interface IUserChannelRepository
{
    Task<IEnumerable<string>> GetChannelIdsForUserAsync(Guid userId);
    Task<bool> ExistsAsync(Guid userId, string channelId);
    Task<int> GetUserCountForChannelAsync(string channelId);
    Task AddAsync(UserChannel link);
    Task RemoveAsync(Guid userId, string channelId);
}