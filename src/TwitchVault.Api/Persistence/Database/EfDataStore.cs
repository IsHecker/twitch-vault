using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database;

/// <summary>
/// Unified write + read abstraction over EF Core, designed for singleton
/// services (background workers, webhook handlers) that can't rely on a
/// scoped DbContext.
///
/// WRITES: wrap a flow in ExecuteAsync(...). Everything inside it — including
/// calls made by other injected collaborator services — shares ONE DbContext
/// and is saved ONCE, automatically, when the flow completes. Entities
/// fetched via FindAsync/QueryAsync inside that flow are auto-tracked; just
/// mutate them directly, no extra call needed. Only brand-new/detached
/// entities (a `new Order()`, something deserialized from a webhook) need an
/// explicit Save() call.
///
/// READS: use QueryAsync(...) for full LINQ power — Include, Where, joins,
/// projections, raw SQL via the DbContext overload. Works both inside an
/// active write flow (joins that flow's tracked context, so mutating a
/// query result WILL be saved) and standalone outside any flow (spins up
/// its own short-lived, no-tracking context — pure read, nothing persists).
/// </summary>
public interface IDataStore
{
    // ---- Write-flow scope ----
    Task ExecuteAsync(Func<Task> flow);
    Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> flow);

    // ---- Writes (call from inside ExecuteAsync) ----
    Task<T?> FindAsync<T>(params object[] keyValues) where T : class;
    Task AddAsync<T>(T entity) where T : class;
    void Save<T>(T entity) where T : class;
    void Delete<T>(T entity) where T : class;
    Task DeleteAsync<TEntity, TKey>(TKey id) where TEntity : Entity<TKey>;
    Task DeleteAsync<T>(params object[] keyValues) where T : class;

    // ---- Reads (work inside OR outside a write flow) ----
    Task<TResult> QueryAsync<T, TResult>(Func<IQueryable<T>, Task<TResult>> query) where T : class;
    Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query);
}

public sealed class EfDataStore(IDbContextFactory<AppDbContext> factory) : IDataStore
{
    private static readonly AsyncLocal<AppDbContext?> _ambient = new();

    // ================= WRITE FLOW SCOPE =================

    public Task ExecuteAsync(Func<Task> flow) =>
        ExecuteAsync(async () => { await flow(); return true; });

    public async Task<TResult> ExecuteAsync<TResult>(Func<Task<TResult>> flow)
    {
        // Already inside an active flow (e.g. a collaborator called
        // ExecuteAsync again) — just join it, don't open a second context
        // or save early.
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

    // ================= WRITES =================

    public async Task<T?> FindAsync<T>(params object[] keyValues) where T : class =>
        await Current().Set<T>().FindAsync(keyValues);

    public async Task AddAsync<T>(T entity) where T : class
    {
        var ctx = Current();
        await ctx.AddAsync(entity);
    }

    // For entities that did NOT come from FindAsync/QueryAsync — a `new
    // Order()`, or one deserialized from a webhook payload. Entities you
    // fetched inside this flow are already tracked; just mutate them
    // directly and skip this call entirely.
    public void Save<T>(T entity) where T : class
    {
        var ctx = Current();

        if (ctx.ChangeTracker.Entries<T>().Any(e => ReferenceEquals(e.Entity, entity)))
            return; // already tracked — mutations are already picked up on save

        ctx.ChangeTracker.TrackGraph(entity, node =>
            node.Entry.State = node.Entry.IsKeySet ? EntityState.Modified : EntityState.Added);
    }

    public void Delete<T>(T entity) where T : class
    {
        var ctx = Current();
        if (ctx.Entry(entity).State == EntityState.Detached)
            ctx.Set<T>().Attach(entity);
        ctx.Set<T>().Remove(entity);
    }

    public async Task DeleteAsync<T>(params object[] keyValues) where T : class
    {
        var ctx = Current();
        var entity = await ctx.Set<T>().FindAsync(keyValues);
        if (entity is not null)
            ctx.Set<T>().Remove(entity);
    }

    public async Task DeleteAsync<TEntity, TKey>(TKey id) where TEntity : Entity<TKey>
    {
        var ctx = Current();
        var entity = await ctx.Set<TEntity>().Where(entity => entity.Id!.Equals(id)).ExecuteDeleteAsync();
    }

    // ================= READS =================

    // Typed IQueryable access — Include, Where, projections, joins, all
    // available. Inside a flow: tracked, mutating results will be saved.
    // Outside a flow: no-tracking, pure read, own short-lived context.
    public async Task<TResult> QueryAsync<T, TResult>(Func<IQueryable<T>, Task<TResult>> query) where T : class
    {
        if (_ambient.Value is not null)
            return await query(_ambient.Value.Set<T>());

        await using var ctx = await factory.CreateDbContextAsync();
        return await query(ctx.Set<T>().AsNoTracking());
    }

    // Raw DbContext access for anything the typed overload can't express:
    // multiple DbSets in one query, raw SQL (FromSqlRaw/ExecuteSqlRaw for
    // reads), complex cross-entity projections. Same tracked/no-tracking
    // rule as above depending on whether a flow is active.
    public async Task<TResult> QueryAsync<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        if (_ambient.Value is not null)
            return await query(_ambient.Value);

        await using var ctx = await factory.CreateDbContextAsync();
        return await query(ctx);
    }

    // ================= INTERNAL =================

    private static AppDbContext Current() =>
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
                        throw; // row was deleted underneath us — real conflict, not noise

                    entry.OriginalValues.SetValues(dbValues);
                }
            }
        }
    }
}

/*
============================== REGISTRATION ==============================
 
builder.Services.AddPooledDbContextFactory<AppDbContext>(opt =>
    opt.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));
 
builder.Services.AddSingleton<IDataStore, EfDataStore>();
 
================================= USAGE ===================================
 
// Pure read, anywhere, no flow needed — safe from any singleton service:
var recentOrders = await _store.QueryAsync<Order, List<Order>>(q =>
    q.Where(o => o.CreatedAt > cutoff)
     .Include(o => o.Payments)
     .ToListAsync());
 
// Write flow: fetch + mutate directly, collaborators join automatically:
await _store.ExecuteAsync(async () =>
{
    var order = await _store.FindAsync<Order>(orderId);
    order.Status = OrderStatus.Paid;                 // auto-tracked, just mutate
 
    await _inventoryService.ReserveStock(order);      // collaborator joins same flow/context
});
 
// Collaborator service — no ctx parameter, no idea a flow exists:
public async Task ReserveStock(Order order)
{
    var item = await _store.FindAsync<InventoryItem>(order.ItemId);
    item.Reserved += order.Quantity;                  // auto-tracked, picked up on save
}
 
// Brand-new entity (e.g. built from a webhook payload) needs one Save() call:
await _store.ExecuteAsync(() =>
{
    var payment = new Payment { OrderId = orderId, Amount = amount };
    _store.Save(payment);
    return Task.CompletedTask;
});
 
============================================================================
*/