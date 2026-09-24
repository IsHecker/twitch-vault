
namespace TwitchVault.Api.Features.Twitch.EventSub;

public interface ITwitchSubscriptionService
{
    Task InitializeSubscriptionsAsync(CancellationToken cancellationToken);
    Task ClearAllSubscriptionsAsync(CancellationToken cancellationToken);
    Task AddChannelsAsync(ICollection<Channel> channels, CancellationToken cancellationToken);
    Task RemoveChannelAsync(Channel channel, CancellationToken cancellationToken);
    Task<bool?> AddChannelEventAsync(string channelId, string eventType, string version, CancellationToken cancellationToken);
    Task<bool?> RemoveChannelEventAsync(string channelId, string eventType, CancellationToken cancellationToken);
}