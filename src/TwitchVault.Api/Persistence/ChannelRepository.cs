using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence;

public class ChannelRepository(JsonDatabase db) : IChannelRepository
{
    public async Task<List<Channel>> GetAllAsync()
    {
        return (await db.ReadAsync()).Channels;
    }

    public async Task<List<Channel>> GetByIdsAsync(IEnumerable<string> ids)
    {
        var idSet = new HashSet<string>(ids);
        return (await GetAllAsync()).Where(c => idSet.Contains(c.Id)).ToList();
    }

    public async Task<Channel?> GetByIdAsync(string channelId)
    {
        return (await GetAllAsync()).FirstOrDefault(c => c.Id == channelId);
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
            var index = data.Channels.FindIndex(c => c.Id == channel.Id);
            if (index >= 0)
                data.Channels[index] = channel;
        });

    public Task DeleteAsync(string channelId) =>
        db.WriteAsync(data => data.Channels.RemoveAll(c => c.Id == channelId));

    public Task SetLiveAsync(string channelId, bool isLive) =>
        db.WriteAsync(data =>
        {
            var channel = data.Channels.FirstOrDefault(c => c.Id == channelId);
            if (channel is null)
                return;
            channel.IsLive = isLive;
        });

    public Task UpdateLastStreamedAtAsync(string channelId, DateTime lastStreamedAt) =>
        db.WriteAsync(data =>
        {
            var channel = data.Channels.FirstOrDefault(c => c.Id == channelId);
            if (channel is not null)
                channel.LastStreamedAt = lastStreamedAt;
        });
}