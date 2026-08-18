using System.Collections.Concurrent;

namespace TwitchVault.Api.Events;

public class EventBus(ILogger<EventBus> logger)
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        var handlers = _handlers.GetOrAdd(typeof(TEvent), _ => []);
        lock (handlers)
        {
            handlers.Add(handler);
        }
    }

    public void UnSubscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        lock (handlers)
        {
            handlers.Remove(handler);
        }
    }

    public async Task PublishAsync<TEvent>(TEvent e)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        List<Delegate> snapshot;
        lock (handlers)
        {
            snapshot = [.. handlers];
        }

        foreach (var handler in snapshot)
        {
            try
            {
                await ((Func<TEvent, Task>)handler)(e);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error publishing event {EventType} to handler.", typeof(TEvent).Name);
            }
        }
    }
}