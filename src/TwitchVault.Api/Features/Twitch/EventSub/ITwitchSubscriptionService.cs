using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Twitch.EventSub;

public interface ITwitchSubscriptionService
{
    Task InitializeSubscriptionsAsync(CancellationToken cancellationToken);
    Task ClearAllSubscriptionsAsync(CancellationToken cancellationToken);
    Task AddChannelsAsync(ICollection<Channel> channels, CancellationToken cancellationToken);
    Task RemoveChannelAsync(Channel channel, CancellationToken cancellationToken);
}