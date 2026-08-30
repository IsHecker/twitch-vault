using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;

namespace TwitchVault.Api.Persistence.Database;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
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
}