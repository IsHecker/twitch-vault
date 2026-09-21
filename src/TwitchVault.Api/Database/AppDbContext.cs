using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; init; }
    public DbSet<Channel> Channels { get; init; }
    public DbSet<TwitchVault.Api.Features.Streams.Stream> Streams { get; init; }
    public DbSet<Subscription> Subscriptions { get; init; }
    public DbSet<BannedChannel> BannedChannels { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.StoreAllEnumsAsNames();
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}