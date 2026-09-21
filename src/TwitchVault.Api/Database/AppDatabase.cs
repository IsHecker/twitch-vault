using Stream = TwitchVault.Api.Features.Streams.Stream;

namespace TwitchVault.Api.Database;

public class AppDatabase
{
    public List<Channel> Channels { get; set; } = [];
    public List<Stream> Streams { get; set; } = [];
    public List<User> Users { get; set; } = [];
    public List<Subscription> Subscriptions { get; set; } = [];
}