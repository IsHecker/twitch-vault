using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.HLS;

public class GetSegment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/hls/{streamId}/segments/{segment}", async (string streamId, string segment, StreamRepository streamRepo, ChannelRepository channelRepo, IWebHostEnvironment env) =>
        {
            var stream = await streamRepo.GetByIdAsync(streamId);
            if (stream is null)
            {
                var channel = await channelRepo.GetByNameAsync(streamId);

                if (channel is null || !channel.IsLive)
                    return Results.NotFound();

                var streams = await streamRepo.GetByChannelIdAsync(channel.ChannelId);
                stream = streams.OrderByDescending(s => s.StartedAt)
                    .FirstOrDefault(s => s.Status == StreamStatus.Recording);

                if (stream is null)
                    return Results.NotFound();
            }

            var safeFileName = Path.GetFileName(segment);
            var filePath = Path.Combine(env.ContentRootPath, stream.FolderPath, safeFileName);

            if (!File.Exists(filePath))
                return Results.NotFound();

            return Results.File(filePath, GetContentType(safeFileName), enableRangeProcessing: true);
        })
        .WithName(nameof(GetSegment))
        .WithTags("HLS")
        .WithSummary("Get a segment file for a stream")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

    public static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            _ => "application/octet-stream"
        };
}