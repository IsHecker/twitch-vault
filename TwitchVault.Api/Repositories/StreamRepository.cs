namespace TwitchVault.Api.Repositories;

public class StreamRepository(JsonDatabase db)
{
    public async Task<List<Models.Stream>> GetAllAsync()
    {
        return (await db.ReadAsync()).Streams;
    }

    public async Task<Models.Stream?> GetByIdAsync(string id)
    {
        return (await GetAllAsync()).FirstOrDefault(s => s.TwitchStreamId == id);
    }

    public async Task<List<Models.Stream>> GetByChannelIdAsync(int channelId)
    {
        var data = await db.ReadAsync();
        return data.Streams.Where(s => s.ChannelId == channelId).ToList();
    }

    public Task AddAsync(Models.Stream stream) =>
        db.WriteAsync(data =>
        {
            if (data.Streams.Any(s =>
                s.TwitchStreamId == stream.TwitchStreamId
                && s.Title.Equals(stream.Title, StringComparison.OrdinalIgnoreCase)))
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

    public Task DeleteAsync(string id) =>
        db.WriteAsync(data => data.Streams.RemoveAll(s => s.TwitchStreamId == id));
}