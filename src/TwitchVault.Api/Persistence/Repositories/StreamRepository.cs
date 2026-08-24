using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Persistence.Repositories;

public class StreamRepository(IDbContextFactory<AppDbContext> contextFactory) : IStreamRepository
{
    public async Task<IEnumerable<Domain.Stream>> GetAllAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Streams.AsNoTracking().ToListAsync();
    }

    public async Task<IEnumerable<Domain.Stream>> ListByChannelIdAsync(string channelId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Streams
            .AsNoTracking()
            .Where(s => s.ChannelId == channelId)
            .ToListAsync();
    }

    public async Task<Domain.Stream?> GetByIdAsync(string twitchStreamId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Streams
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TwitchStreamId == twitchStreamId);
    }

    public async Task AddAsync(Domain.Stream stream)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        var exists = await context.Streams.AnyAsync(s => s.TwitchStreamId == stream.TwitchStreamId);
        if (exists)
            return;

        context.Streams.Add(stream);
        await context.SaveChangesAsync();
    }

    public async Task UpdateAsync(Domain.Stream stream)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        context.Streams.Update(stream);
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string twitchStreamId)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Streams
            .Where(s => s.TwitchStreamId == twitchStreamId)
            .ExecuteDeleteAsync();
    }
}