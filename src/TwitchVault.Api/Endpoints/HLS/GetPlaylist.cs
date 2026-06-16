using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.HLS;

public class GetPlaylist : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/hls/{streamId}/playlist.m3u8", async (
            string streamId,
            IStreamRepository streamRepo,
            ChannelRepository channelRepo,
            IWebHostEnvironment env) =>
        {
            var stream = await streamRepo.GetStreamByIdAsync(streamId);
            if (stream is null)
            {
                var channel = await channelRepo.GetByNameAsync(streamId);
                if (channel is null || !channel.IsLive)
                    return Results.NotFound();
                var streams = await streamRepo.GetStreamsByChannelIdAsync(channel.ChannelId);
                stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();
                if (stream is null || stream.Status != StreamStatus.Recording)
                    return Results.NotFound();
            }

            var playlistPath = Path.Combine(env.ContentRootPath, stream.FolderPath, "playlist.m3u8");
            if (!File.Exists(playlistPath))
                return Results.NotFound();
            return Results.File(playlistPath, "application/vnd.apple.mpegurl", enableRangeProcessing: true);
        })
        .WithName(nameof(GetPlaylist))
        .WithTags("HLS")
        .WithSummary("Get the HLS playlist for a live or finished stream")
        .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl")
        .Produces(StatusCodes.Status404NotFound);
}