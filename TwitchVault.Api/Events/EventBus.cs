namespace TwitchVault.Api.Events;

public class EventBus
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];

    public void Subscribe<TEvent>(Action<TEvent> handler)
    {
        var type = typeof(TEvent);
        if (!_handlers.TryGetValue(type, out var handlers))
            _handlers[type] = handlers = [];
        handlers.Add(handler);
    }

    public void Publish<TEvent>(TEvent e)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlers)) return;
        foreach (var handler in handlers)
            ((Action<TEvent>)handler)(e);
    }
}

public record QualityChangedEvent(string ChannelName, int QualityRank);
public record ChannelAddedEvent(int ChannelId);
public record ChannelRemovedEvent(int ChannelId);