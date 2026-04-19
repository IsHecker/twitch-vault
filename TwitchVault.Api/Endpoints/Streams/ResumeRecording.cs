using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Endpoints.Streams;

public class ResumeRecording : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPatch("/api/streams/{streamId}/resume", async (
            string streamId,
            StreamRepository streamRepository,
            ChannelRepository channelRepository,
            StreamController streamController,
            TwitchClient twitchClient) =>
        {
            var stream = await streamRepository.GetByIdAsync(streamId);
            if (stream is null)
                return Results.NotFound();

            if (stream.Status != StreamStatus.Stopped)
                return Results.BadRequest("Stream is not stopped.");

            var channel = await channelRepository.GetByIdAsync(stream.ChannelId);
            if (channel is null)
                return Results.NotFound();

            var metadata = await twitchClient.GetStreamMetadataAsync(channel.Name, default);
            if (metadata is null)
            {
                stream.Status = StreamStatus.Finished;
                await streamRepository.UpdateAsync(stream);

                return Results.BadRequest("Channel is not currently live.");
            }

            if (metadata.Value.TwitchStreamId != stream.TwitchStreamId)
                return Results.BadRequest("A different stream is now live on this channel.");

            await streamController.ResumeSessionAsync(stream, channel);
            return Results.NoContent();
        })
        .WithName(nameof(ResumeRecording))
        .WithTags("Streams")
        .WithSummary("Resume a paused or interrupted recording")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);
}