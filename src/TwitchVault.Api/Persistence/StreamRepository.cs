namespace TwitchVault.Api.Persistence;

public class StreamRepository(JsonDatabase db) : IStreamRepository
{
    public async Task<List<Domain.Stream>> GetStreamsByChannelIdAsync(string channelId)
    {
        var data = await db.ReadAsync();
        return data.Streams.Where(s => s.ChannelId == channelId).ToList();
    }

    public async Task<Domain.Stream?> GetStreamByIdAsync(string twitchStreamId)
    {
        var data = await db.ReadAsync();
        return data.Streams.FirstOrDefault(s => s.TwitchStreamId == twitchStreamId);
    }

    public Task AddAsync(Domain.Stream stream) =>
        db.WriteAsync(data =>
        {
            if (data.Streams.Any(s => s.TwitchStreamId == stream.TwitchStreamId))
                return;
            data.Streams.Add(stream);
        });
    public Task UpdateAsync(Domain.Stream stream) =>
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