using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Repositories;

public class ChannelRepository(IDbContextFactory<AppDbContext> contextFactory) : IChannelRepository
{
    public async Task<IEnumerable<Channel>> GetAllAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return context.Channels.AsNoTracking();
    }

    public async Task<IEnumerable<Channel>> GetByIdsAsync(IEnumerable<string> ids)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return context.Channels
            .AsNoTracking()
            .Where(c => ids.Contains(c.Id));
    }

    public async Task<Channel?> GetByIdAsync(string channelId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == channelId);
    }

    public async Task<Channel?> GetByNameAsync(string name)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Channels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task AddAsync(Channel channel)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        context.Channels.Add(channel);
    }

    public async Task UpdateAsync(Channel channel)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        context.Channels.Update(channel);
    }

    public async Task DeleteAsync(string channelId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Channels
            .Where(c => c.Id == channelId)
            .ExecuteDeleteAsync();
    }

    public async Task SetLiveAsync(string channelId, bool isLive)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Channels
            .Where(c => c.Id == channelId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.IsLive, isLive));
    }

    public async Task UpdateLastStreamedAtAsync(string channelId, DateTime lastStreamedAt)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Channels
            .Where(c => c.Id == channelId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.LastStreamedAt, lastStreamedAt));
    }
}