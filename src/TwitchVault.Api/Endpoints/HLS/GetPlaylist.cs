using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.HLS;

public class GetPlaylist : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/hls/{streamId}/playlist.m3u8", async (
            string streamId,
            IStreamRepository streamRepo,
            IWebHostEnvironment env) =>
        {
            var stream = await streamRepo.GetByIdAsync(streamId);
            if (stream is null)
                return Results.NotFound();

            var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);

            if (!File.Exists(playlistPath))
                return Results.NotFound();

            return Results.File(playlistPath, "application/vnd.apple.mpegurl");
        })
        .WithName(nameof(GetPlaylist))
        .WithTags("HLS")
        .WithSummary("Get the HLS playlist for a live or finished stream")
        .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl")
        .Produces(StatusCodes.Status404NotFound);
}