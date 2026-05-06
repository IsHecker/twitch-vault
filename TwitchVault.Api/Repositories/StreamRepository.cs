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

    public Task UpdateStreamAsync(Models.Stream stream) =>
        db.WriteAsync(data =>
        {
            var index = data.Streams.FindIndex(s => s.TwitchStreamId == stream.TwitchStreamId);
            if (index >= 0)
                data.Streams[index] = stream;
        });

    public async Task<List<StreamSegment>> GetSegmentsByStreamIdAsync(string twitchStreamId)
    {
        var data = await db.ReadAsync();
        return data.StreamSegments
            .Where(s => s.StreamId == twitchStreamId)
            .OrderBy(s => s.SegmentNumber)
            .ToList();
    }

    public async Task<StreamSegment?> GetActiveSegmentAsync(string twitchStreamId)
    {
        var data = await db.ReadAsync();
        return data.StreamSegments.FirstOrDefault(s => s.StreamId == twitchStreamId && s.Status == StreamStatus.Recording);
    }

    public Task AddSegmentAsync(StreamSegment segment) =>
        db.WriteAsync(data =>
        {
            data.StreamSegments.Add(segment);
        });

    public Task UpdateSegmentAsync(StreamSegment segment) =>
        db.WriteAsync(data =>
        {
            var index = data.StreamSegments.FindIndex(s => s.Id == segment.Id);
            if (index >= 0)
                data.StreamSegments[index] = segment;
        });

    public Task DeleteStreamAsync(string twitchStreamId) =>
        db.WriteAsync(data =>
        {
            data.Streams.RemoveAll(s => s.TwitchStreamId == twitchStreamId);
            data.StreamSegments.RemoveAll(s => s.StreamId == twitchStreamId);
        });

    public Task DeleteSegmentAsync(StreamSegment segment) =>
        db.WriteAsync(data =>
        {
            data.StreamSegments.RemoveAll(s => s.Id == segment.Id);
        });
}