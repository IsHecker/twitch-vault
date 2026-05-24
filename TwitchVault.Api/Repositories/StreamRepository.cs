using TwitchVault.Api.Models;

namespace TwitchVault.Api.Repositories;

public class StreamRepository(JsonDatabase db)
{
    public async Task<List<Models.Stream>> GetAllStreamsAsync()
    {
        return (await db.ReadAsync()).Streams;
    }

    public async Task<List<Models.Stream>> GetStreamsByChannelIdAsync(string channelId)
    {
        var data = await db.ReadAsync();
        return data.Streams.Where(s => s.ChannelId == channelId).ToList();
    }

    public async Task<Models.Stream?> GetStreamByIdAsync(string twitchStreamId)
    {
        var data = await db.ReadAsync();
        return data.Streams.FirstOrDefault(s => s.TwitchStreamId == twitchStreamId);
    }

    public Task AddStreamAsync(Models.Stream stream) =>
        db.WriteAsync(data =>
        {
            if (data.Streams.Any(s => s.TwitchStreamId == stream.TwitchStreamId))
                return;

            data.Streams.Add(stream);
        });

    public Task UpdateAsync(Models.Stream stream) =>
        db.WriteAsync(data =>
        {
            var index = data.Streams.FindIndex(s => s.TwitchStreamId == stream.TwitchStreamId);
            if (index >= 0)
                data.Streams[index] = stream;
        });

    public Task DeleteStreamAsync(string twitchStreamId) =>
        db.WriteAsync(data =>
        {
            data.Streams.RemoveAll(s => s.TwitchStreamId == twitchStreamId);
        });
}