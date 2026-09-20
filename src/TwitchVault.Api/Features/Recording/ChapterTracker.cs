namespace TwitchVault.Api.Features.Recording;

public interface IChapterTracker
{
    void Attach(TwitchVault.Api.Features.Streams.Stream stream, Channel channel);
    void Dispose();
}

public sealed class ChapterTracker(
    EventBus eventBus,
    IDataStore dataStore,
    IDateTimeProvider dateTimeProvider) : IDisposable, IChapterTracker
{
    private TwitchVault.Api.Features.Streams.Stream _stream = null!;
    private Channel _channel = null!;
    private Func<ChannelUpdateEvent, Task> _handler = null!;

    public void Attach(TwitchVault.Api.Features.Streams.Stream stream, Channel channel)
    {
        _handler = OnMetadataChangedAsync;
        _stream = stream;
        _channel = channel;
        eventBus.Subscribe(channel.Id, _handler);
    }

    private async Task OnMetadataChangedAsync(ChannelUpdateEvent e)
    {
        if (_stream.CurrentChapter.Title == e.Title
            && _stream.CurrentChapter.CategoryId == e.CategoryId)
            return;

        await dataStore.ExecuteAsync(() =>
        {
            dataStore.Save(_stream);
            _stream.AddChapter(e.Title, e.CategoryId, dateTimeProvider.DateTimeNow);
            return Task.CompletedTask;
        });
    }

    public void Dispose()
    {
        if (_handler != null)
            eventBus.UnSubscribe<ChannelUpdateEvent>(_channel.Id);
    }
}