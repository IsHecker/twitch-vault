using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database;

public interface IDataStore
{
    Task ExecuteAsync(Func<Task> flow);
    Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> flow);

    Task<T?> FindAsync<T>(params object[] keyValues) where T : class;
    Task AddAsync<T>(T entity) where T : class;
    void Save<T>(T entity) where T : class;
    void Delete<T>(T entity) where T : class;
    Task DeleteAsync<TEntity, TKey>(TKey id) where TEntity : Entity<TKey>;
    Task DeleteAsync<T>(params object[] keyValues) where T : class;

    Task<TResult> QueryAsync<T, TResult>(Func<IQueryable<T>, Task<TResult>> query) where T : class;
    Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query);
}

public sealed class EfDataStore(IDbContextFactory<AppDbContext> factory) : IDataStore
{
    private static readonly AsyncLocal<AppDbContext?> _ambient = new();

    public Task ExecuteAsync(Func<Task> flow) =>
        ExecuteAsync(async () => { await flow(); return true; });

    public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> flow)
    {
        if (_ambient.Value is not null)
            return await flow();

        await using var ctx = await factory.CreateDbContextAsync();
        var strategy = ctx.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            _ambient.Value = ctx;
            try
            {
                var result = await flow();
                await SaveWithConcurrencyRetryAsync(ctx);
                return result;
            }
            finally
            {
                _ambient.Value = null;
            }
        });
    }

    public async Task<T?> FindAsync<T>(params object[] keyValues) where T : class =>
        await CurrentDbContext().Set<T>().FindAsync(keyValues);

    public async Task AddAsync<T>(T entity) where T : class
    {
        var ctx = CurrentDbContext();
        await ctx.AddAsync(entity);
    }

    public void Save<T>(T entity) where T : class
    {
        var ctx = CurrentDbContext();

        if (ctx.ChangeTracker.Entries<T>().Any(e => ReferenceEquals(e.Entity, entity)))
            return;

        ctx.ChangeTracker.TrackGraph(entity, node =>
            node.Entry.State = node.Entry.IsKeySet ? EntityState.Modified : EntityState.Added);
    }

    public void Delete<T>(T entity) where T : class
    {
        var ctx = CurrentDbContext();
        if (ctx.Entry(entity).State == EntityState.Detached)
            ctx.Set<T>().Attach(entity);
        ctx.Set<T>().Remove(entity);
    }

    public async Task DeleteAsync<T>(params object[] keyValues) where T : class
    {
        var ctx = CurrentDbContext();
        var entity = await ctx.Set<T>().FindAsync(keyValues);
        if (entity is not null)
            ctx.Set<T>().Remove(entity);
    }

    public async Task DeleteAsync<TEntity, TKey>(TKey id) where TEntity : Entity<TKey>
    {
        var ctx = CurrentDbContext();
        var entity = await ctx.Set<TEntity>().Where(entity => entity.Id!.Equals(id)).ExecuteDeleteAsync();
    }

    public async Task<TResult> QueryAsync<T, TResult>(Func<IQueryable<T>, Task<TResult>> query) where T : class
    {
        if (_ambient.Value is not null)
            return await query(_ambient.Value.Set<T>());

        await using var ctx = await factory.CreateDbContextAsync();
        return await query(ctx.Set<T>().AsNoTracking());
    }

    public async Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        if (_ambient.Value is not null)
            return await query(_ambient.Value);

        await using var ctx = await factory.CreateDbContextAsync();
        return await query(ctx);
    }

    private static AppDbContext CurrentDbContext() =>
        _ambient.Value ?? throw new InvalidOperationException(
            "No active write scope. Wrap this call in _store.ExecuteAsync(...) first.");

    private static async Task SaveWithConcurrencyRetryAsync(AppDbContext ctx, int maxRetries = 3)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await ctx.SaveChangesAsync();
                return;
            }
            catch (DbUpdateConcurrencyException ex) when (attempt < maxRetries)
            {
                foreach (var entry in ex.Entries)
                {
                    var dbValues = await entry.GetDatabaseValuesAsync();
                    if (dbValues is null)
                        throw;

                    entry.OriginalValues.SetValues(dbValues);
                }
            }
        }
    }
}