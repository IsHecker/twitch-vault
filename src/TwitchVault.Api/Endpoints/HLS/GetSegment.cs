using Microsoft.AspNetCore.Mvc;
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
            IWebHostEnvironment env) =>
        {
            var stream = await streamRepo.GetByIdAsync(streamId);
            if (stream is null)
                return Results.NotFound();

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
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound);

    public static string GetContentType(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            _ => "application/octet-stream"
        };
}