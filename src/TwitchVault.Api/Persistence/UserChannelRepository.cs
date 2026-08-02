using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence;

public class UserChannelRepository(JsonDatabase db) : IUserChannelRepository
{
    public async Task<List<string>> GetChannelIdsForUserAsync(Guid userId)
    {
        var data = await db.ReadAsync();
        return data.UserChannels
            .Where(uc => uc.UserId == userId)
            .Select(uc => uc.ChannelId)
            .ToList();
    }

    public async Task<bool> ExistsAsync(Guid userId, string channelId)
    {
        var data = await db.ReadAsync();
        return data.UserChannels.Any(uc => uc.UserId == userId && uc.ChannelId == channelId);
    }

    public async Task<int> GetUserCountForChannelAsync(string channelId)
    {
        var data = await db.ReadAsync();
        return data.UserChannels.Count(uc => uc.ChannelId == channelId);
    }

    public Task AddAsync(UserChannel link) =>
        db.WriteAsync(data => data.UserChannels.Add(link));

    public Task RemoveAsync(Guid userId, string channelId) =>
        db.WriteAsync(data =>
            data.UserChannels.RemoveAll(uc => uc.UserId == userId && uc.ChannelId == channelId));
}