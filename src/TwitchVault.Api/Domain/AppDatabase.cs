namespace TwitchVault.Api.Domain;

public class AppDatabase
{
    public List<Channel> Channels { get; set; } = [];
    public List<Stream> Streams { get; set; } = [];
    public List<User> Users { get; set; } = [];
    public List<UserChannel> UserChannels { get; set; } = [];
}