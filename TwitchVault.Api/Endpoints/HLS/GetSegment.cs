using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.HLS;

public class GetSegment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/hls/{streamId}/segments/{segmentName}", async (
            string streamId,
            string segmentName,
            [FromQuery] int? segment,
            [FromQuery] bool? audioOnly,
            StreamRepository streamRepo,
            ChannelRepository channelRepo,
            IWebHostEnvironment env) =>
        {
            /*
            
            var stream = await streamRepo.GetStreamByIdAsync(streamId);
            if (stream is null)
            {
                var channel = await channelRepo.GetByNameAsync(streamId);

                if (channel is null || !channel.IsLive)
                    return Results.NotFound();

                var streams = await streamRepo.GetStreamsByChannelIdAsync(channel.ChannelId);
                stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();

                if (stream is null || stream.StreamSegment.Status != StreamStatus.Recording)
                    return Results.NotFound();
            }

            StreamSegment? targetSegment = stream.StreamSegment;

            if (segment.HasValue && segment != targetSegment.SegmentNumber)
            {
                // 1. User asked for a specific chapter/segment
                var segments = await streamRepo.GetSegmentsByStreamIdAsync(streamId);
                targetSegment = segments.FirstOrDefault(s => s.SegmentNumber == segment.Value);
            }

            if (targetSegment == null)
                return Results.NotFound();

            var playlistPath = Path.Combine(env.ContentRootPath, targetSegment.FolderPath, "playlist.m3u8");

            if (!File.Exists(playlistPath))
                return Results.NotFound();

            return Results.File(playlistPath, "application/vnd.apple.mpegurl", enableRangeProcessing: true);
            
            */
            var stream = await streamRepo.GetStreamByIdAsync(streamId);
            if (stream is null)
            {
                var channel = await channelRepo.GetByNameAsync(streamId);

                if (channel is null || !channel.IsLive)
                    return Results.NotFound();

                var streams = await streamRepo.GetStreamsByChannelIdAsync(channel.ChannelId);
                stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();

                if (stream is null || stream.StreamSegment.Status != StreamStatus.Recording)
                    return Results.NotFound();
            }

            StreamSegment? targetSegment = stream.StreamSegment;
            if (segment.HasValue && segment != targetSegment.SegmentNumber)
            {
                var segments = await streamRepo.GetSegmentsByStreamIdAsync(streamId);
                targetSegment = segments.FirstOrDefault(s => s.SegmentNumber == segment.Value);
            }

            if (targetSegment == null)
                return Results.NotFound();

            var safeFileName = Path.GetFileName(segmentName);
            var filePath = Path.Combine(env.ContentRootPath, targetSegment.FolderPath, safeFileName);

            if (!File.Exists(filePath))
                return Results.NotFound();

            return Results.File(filePath, GetContentType(safeFileName), enableRangeProcessing: true);

            // var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            // var arguments = $"-i \"{filePath}\" -vn -acodec copy -f adts -";

            // var startInfo = new ProcessStartInfo
            // {
            //     FileName = ffmpegPath,
            //     Arguments = arguments,
            //     RedirectStandardOutput = true,
            //     UseShellExecute = false,
            //     CreateNoWindow = true
            // };

            // var process = Process.Start(startInfo);
            // if (process == null)
            //     return Results.StatusCode(500);

            // return Results.Stream(process.StandardOutput.BaseStream, "audio/aac");
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