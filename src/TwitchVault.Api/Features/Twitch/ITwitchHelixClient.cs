namespace TwitchVault.Api.Features.Twitch;

public readonly record struct TwitchVodInfo(string Id, DateTime CreatedAt);

public interface ITwitchHelixClient
{
    IAsyncEnumerable<EventSubSubscription> GetEventSubSubscriptionsAsync(
        string status = "",
        CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> CreateEventSubSubscriptionAsync(
        string channelId,
        string type,
        string version,
        CancellationToken cancellationToken);

    Task<HttpResponseMessage> DeleteEventSubSubscriptionAsync(string id, CancellationToken cancellationToken);

    Task<List<(string UserId, string UserLogin, int ViewerCount)>> GetLiveStreamsAsync(
        int count,
        string language = "",
        int minViewers = 0,
        int maxViewers = int.MaxValue,
        int maxPages = 20,
        CancellationToken cancellationToken = default);

    Task<TwitchVodInfo?> GetRecentStreamVodAsync(
        string userId,
        string streamId,
        CancellationToken cancellationToken = default);
}