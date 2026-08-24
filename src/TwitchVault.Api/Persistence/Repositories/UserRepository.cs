using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Repositories;

public class UserRepository(IDbContextFactory<AppDbContext> contextFactory) : IUserRepository
{
    public async Task<IEnumerable<User>> GetAllAsync()
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users.AsNoTracking().ToListAsync();
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
    }

    public async Task<User?> GetByIdAsync(Guid id)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        return await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task AddAsync(User user)
    {
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Users.AddAsync(user);
    }
}