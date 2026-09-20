using System.Collections.Concurrent;

namespace TwitchVault.Api.Infrastructure.Events;

public class EventBus(ILogger<EventBus> logger)
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<object, Delegate>> _keyedHandlers = [];

    public void Subscribe<TEvent>(object key, Func<TEvent, Task> handler)
        => _keyedHandlers.GetOrAdd(typeof(TEvent), _ => new())[key] = handler;

    public void UnSubscribe<TEvent>(object key)
    {
        if (_keyedHandlers.TryGetValue(typeof(TEvent), out var dict))
            dict.TryRemove(key, out _);
    }

    public async Task PublishAsync<TEvent>(object key, TEvent e)
    {
        if (!_keyedHandlers.TryGetValue(typeof(TEvent), out var dict) || !dict.TryGetValue(key, out var keyedDelegate))
            return;

        try { await ((Func<TEvent, Task>)keyedDelegate)(e); }
        catch (Exception ex) { logger.LogError(ex, "Error in keyed handler for event {EventType}.", typeof(TEvent).Name); }
        return;
    }
}