using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Streams.Endpoints;

public class GetPlaylist : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/{streamId}/playlist.m3u8", async (
            string streamId,
            ClaimsPrincipal principal,
            AppDbContext db,
            IWebHostEnvironment env) =>
        {
            var stream = await db.Streams.AsNoTracking().GetByIdAsync(streamId);
            if (stream is null
                || stream.StorageLocation != StorageLocation.Remote
                || stream.StorageOperationStatus != StorageOperationStatus.Uploaded)
            {
                return Results.NotFound();
            }

            if (!principal.IsInRole("Admin"))
            {
                var isBanned = await db.BannedChannels.AsNoTracking().AnyAsync(b => b.Id == stream.ChannelId);
                if (isBanned)
                    return Results.NotFound();

                var userId = principal.GetUserId();
                var subscription = await db.Subscriptions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChannelId == stream.ChannelId);

                if (subscription is null || stream.StartedAt < subscription.AddedAt)
                    return Results.NotFound();
            }

            var playlistPath = stream.Folder.GetAbsolutePlaylistPath(env.ContentRootPath);
            if (!File.Exists(playlistPath))
                return Results.NotFound();

            return Results.File(playlistPath, "application/vnd.apple.mpegurl");
        })
        .RequireAuthorization()
        .WithName(nameof(GetPlaylist))
        .WithTags("HLS")
        .WithSummary("Get the HLS playlist for a finished stream")
        .Produces(StatusCodes.Status200OK, contentType: "application/vnd.apple.mpegurl")
        .Produces(StatusCodes.Status404NotFound);
}