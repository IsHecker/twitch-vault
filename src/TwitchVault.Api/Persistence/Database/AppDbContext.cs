using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Persistence.Database;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Channel> Channels { get; }
    DbSet<Domain.Stream> Streams { get; }
    DbSet<UserChannel> UserChannels { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options), IAppDbContext
{
    public DbSet<User> Users { get; init; }
    public DbSet<Channel> Channels { get; init; }
    public DbSet<Domain.Stream> Streams { get; init; }
    public DbSet<UserChannel> UserChannels { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.StoreAllEnumsAsNames();
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }

    public async Task<int> ExecuteSqlAsync(string sql, CancellationToken cancellationToken = default)
    {
        return await Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}