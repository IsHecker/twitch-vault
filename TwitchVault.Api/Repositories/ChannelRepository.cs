using TwitchVault.Api.Models;

namespace TwitchVault.Api.Repositories;

public class ChannelRepository(JsonDatabase db)
{
    public async Task<List<Channel>> GetAllAsync()
    {
        return (await db.ReadAsync()).Channels;
    }

    public async Task<Channel?> GetByIdAsync(int id)
    {
        return (await GetAllAsync()).FirstOrDefault(c => c.ChannelId == id);
    }

    public async Task<Channel?> GetByNameAsync(string name)
    {
        return (await GetAllAsync()).FirstOrDefault(c => c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public Task AddAsync(Channel channel) =>
        db.WriteAsync(data =>
        {
            if (data.Channels.Any(c => c.Name.Equals(channel.Name, StringComparison.OrdinalIgnoreCase)))
                return;

            data.Channels.Add(channel);
        });

    public Task UpdateAsync(Channel channel) =>
        db.WriteAsync(data =>
        {
            var index = data.Channels.FindIndex(c => c.ChannelId == channel.ChannelId);
            if (index >= 0)
                data.Channels[index] = channel;
        });

    public Task DeleteAsync(int id) =>
        db.WriteAsync(data => data.Channels.RemoveAll(c => c.ChannelId == id));

    public Task SetLiveAsync(int channelId, bool isLive) =>
        db.WriteAsync(data =>
        {
            var channel = data.Channels.FirstOrDefault(c => c.ChannelId == channelId);
            if (channel is null)
                return;
            channel.IsLive = isLive;
        });

    public Task UpdateLastStreamedAtAsync(int channelId, DateTime lastStreamedAt) =>
        db.WriteAsync(data =>
        {
            var channel = data.Channels.FirstOrDefault(c => c.ChannelId == channelId);
            if (channel is not null)
                channel.LastStreamedAt = lastStreamedAt;
        });
}