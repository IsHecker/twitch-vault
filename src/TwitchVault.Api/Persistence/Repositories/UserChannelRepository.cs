using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Repositories;

public class UserChannelRepository(IDbContextFactory<AppDbContext> contextFactory) : IUserChannelRepository
{
    public async Task<IEnumerable<string>> GetChannelIdsForUserAsync(Guid userId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return context.UserChannels
            .AsNoTracking()
            .Where(uc => uc.UserId == userId)
            .Select(uc => uc.ChannelId);
    }

    public async Task<bool> ExistsAsync(Guid userId, string channelId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.UserChannels
            .AsNoTracking()
            .AnyAsync(uc => uc.UserId == userId && uc.ChannelId == channelId);
    }

    public async Task<int> GetUserCountForChannelAsync(string channelId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.UserChannels
            .AsNoTracking()
            .CountAsync(uc => uc.ChannelId == channelId);
    }

    public async Task AddAsync(UserChannel link)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var exists = await context.UserChannels.AnyAsync(uc => uc.UserId == link.UserId && uc.ChannelId == link.ChannelId);
        if (exists)
            return;

        context.UserChannels.Add(link);
        await context.SaveChangesAsync();
    }

    public async Task RemoveAsync(Guid userId, string channelId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.UserChannels
            .Where(uc => uc.UserId == userId && uc.ChannelId == channelId)
            .ExecuteDeleteAsync();
    }
}