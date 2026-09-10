using System.Collections.Concurrent;

namespace TwitchVault.Api.Events;

// public class EventBus(ILogger<EventBus> logger)
// {
//     private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = [];

//     public void Subscribe<TEvent>(Func<TEvent, Task> handler)
//     {
//         var list = _handlers.GetOrAdd(typeof(TEvent), _ => []);
//         lock (list) list.Add(handler);
//     }

//     public void UnSubscribe<TEvent>(Func<TEvent, Task> handler)
//     {
//         if (_handlers.TryGetValue(typeof(TEvent), out var list))
//             lock (list) list.Remove(handler);
//     }

//     public async Task PublishAsync<TEvent>(TEvent e)
//     {
//         if (!_handlers.TryGetValue(typeof(TEvent), out var list))
//             return;

//         List<Delegate> snapshot;
//         lock (list) snapshot = list;

//         foreach (var handler in snapshot)
//         {
//             try { await ((Func<TEvent, Task>)handler)(e); }
//             catch (Exception ex) { logger.LogError(ex, "Error publishing event {EventType}.", typeof(TEvent).Name); }
//         }
//     }
// }




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