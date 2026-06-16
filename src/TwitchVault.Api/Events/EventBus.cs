using System.Collections.Concurrent;

namespace TwitchVault.Api.Events;

public class EventBus(ILogger<EventBus> logger)
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            _handlers[typeof(TEvent)] = handlers = [];

        handlers.Add(handler);
    }

    public void UnSubscribe<TEvent>(Func<TEvent, Task> handler)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers))
            return;

        handlers.Remove(handler);
    }

    public async Task PublishAsync<TEvent>(TEvent e)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;

        foreach (var handler in handlers)
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