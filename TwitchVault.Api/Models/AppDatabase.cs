namespace TwitchVault.Api.Models;

public class AppDatabase
{
    public List<Channel> Channels { get; set; } = [];
    public List<Stream> Streams { get; set; } = [];
}