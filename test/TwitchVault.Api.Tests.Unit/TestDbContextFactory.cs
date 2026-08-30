using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Persistence.Database;

namespace TwitchVault.Api.Tests.Unit;

public sealed class TestDbContextFactory : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public TestDbContextFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new AppDbContext(_options);
        db.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose()
    {
        _connection.Dispose();
    }
}