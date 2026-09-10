using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Events;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Recording;

public interface IChapterTracker
{
    void Attach(Domain.Stream stream, Channel channel);
    void Dispose();
}

// public sealed class ChapterTracker(
//     EventBus eventBus,
//     IDataStore dataStore,
//     IDateTimeProvider dateTimeProvider,
//     ILogger<ChapterTracker> logger) : IDisposable, IChapterTracker
// {
//     private Domain.Stream _stream = null!;
//     private Channel _channel = null!;
//     private Func<ChannelUpdateEvent, Task> _handler = null!;

//     public void Attach(Domain.Stream stream, Channel channel)
//     {
//         _handler = OnMetadataChangedAsync;
//         _stream = stream;
//         _channel = channel;
//         eventBus.Subscribe(_handler);
//     }

//     private async Task OnMetadataChangedAsync(ChannelUpdateEvent e)
//     {
//         if (_channel.Id != e.ChannelId)
//             return;

//         using var _chnlScope = logger.BeginScope("{Channel}", _channel.Name);
//         using var _metaScope = logger.BeginScope("'{Title}' ({CategoryId})", e.Title, e.CategoryId);

//         if (_stream.CurrentChapter.Title == e.Title && _stream.CurrentChapter.CategoryId == e.CategoryId)
//         {
//             logger.LogInformation("Metadata change ignored: title and game unchanged.");
//             return;
//         }

//         logger.LogInformation("Metadata split triggered.");

//         await dataStore.ExecuteAsync(() =>
//         {
//             dataStore.Save(_stream); // came from outside any flow — attach it
//             _stream.AddChapter(e.Title, e.CategoryId, dateTimeProvider.DateTimeNow);
//             return Task.CompletedTask;
//         });
//     }

//     public void Dispose()
//     {
//         if (_handler != null)
//         {
//             eventBus.UnSubscribe(_handler);
//         }
//     }
// }




public sealed class ChapterTracker(
    EventBus eventBus,
    IDataStore dataStore,
    IDateTimeProvider dateTimeProvider,
    ILogger<ChapterTracker> logger) : IDisposable, IChapterTracker
{
    private Domain.Stream _stream = null!;
    private Channel _channel = null!;
    private Func<ChannelUpdateEvent, Task> _handler = null!;

    public void Attach(Domain.Stream stream, Channel channel)
    {
        _handler = OnMetadataChangedAsync;
        _stream = stream;
        _channel = channel;
        eventBus.Subscribe(channel.Id, _handler);
    }

    private async Task OnMetadataChangedAsync(ChannelUpdateEvent e)
    {
        using var _chnlScope = logger.BeginScope("{Channel}", _channel.Name);
        using var _metaScope = logger.BeginScope("'{Title}' ({CategoryId})", e.Title, e.CategoryId);

        if (_stream.CurrentChapter.Title == e.Title && _stream.CurrentChapter.CategoryId == e.CategoryId)
        {
            logger.LogInformation("Metadata change ignored: title and game unchanged.");
            return;
        }

        logger.LogInformation("Metadata split triggered.");

        await dataStore.ExecuteAsync(() =>
        {
            dataStore.Save(_stream); // came from outside any flow — attach it
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