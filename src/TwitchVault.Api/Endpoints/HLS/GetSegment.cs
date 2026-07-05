using Microsoft.AspNetCore.Mvc;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
namespace TwitchVault.Api.Endpoints.HLS;

public class GetSegment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/hls/{streamId}/segments/{segmentName}", async (
            string streamId,
            string segmentName,
            [FromQuery] bool? audioOnly,
            IStreamRepository streamRepo,
            IChannelRepository channelRepo,
            IWebHostEnvironment env) =>
        {
            var stream = await streamRepo.GetStreamByIdAsync(streamId);
            if (stream is null)
            {
                var channel = await channelRepo.GetByNameAsync(streamId);
                if (channel is null || !channel.IsLive)
                    return Results.NotFound();

                var streams = await streamRepo.GetStreamsByChannelIdAsync(channel.Id);
                stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();
                if (stream is null || stream.Status != StreamStatus.Recording)
                    return Results.NotFound();
            }

            var safeFileName = Path.GetFileName(segmentName);
            var filePath = stream.Folder.GetAbsoluteSegmentPath(env.ContentRootPath, safeFileName);
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