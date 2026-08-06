using TwitchVault.Api.Backblaze;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.HLS;

public class GetPlaylist : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/hls/{streamId}/playlist.m3u8", async (
            string streamId,
            IStreamRepository streamRepo,
            BackblazePlaylistRewriter playlistRewriter,
            IWebHostEnvironment env) =>
        {
            var stream = await streamRepo.GetByIdAsync(streamId);
            if (stream is null)
                return Results.NotFound();

            var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);

            if (!File.Exists(playlistPath))
                return Results.NotFound();

            if (stream.Storage != Domain.StorageLocation.Remote)
                return Results.File(playlistPath, "application/vnd.apple.mpegurl");

            var rewritten = await playlistRewriter.RewriteAsync(playlistPath, stream.Folder.RelativePath);

            return Results.Content(rewritten, "application/vnd.apple.mpegurl");
        })
        .WithName(nameof(GetPlaylist))
        .WithTags("HLS")
        .WithSummary("Get the HLS playlist for a live or finished stream")
        .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl")
        .Produces(StatusCodes.Status404NotFound);
}