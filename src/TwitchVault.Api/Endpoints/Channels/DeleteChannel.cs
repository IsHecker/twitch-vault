using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch.EventSub;
using Microsoft.Extensions.Options;

namespace TwitchVault.Api.Endpoints.Channels;

public class DeleteChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/channels/{channelId}", async (
            string channelId,
            TwitchSubscriptionService twitchSubscription,
            ChannelRepository repo,
            StreamController recordingController,
            IStreamRepository streamRepository,
            IOptions<PathsOptions> pathsOptions) =>
        {
            var channel = await repo.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();
            var streams = await streamRepository.GetStreamsByChannelIdAsync(channelId);
            if (channel.IsLive)
            {
                var stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();
                if (stream is not null)
                {
                    await recordingController.ToggleStreamDeletionAsync(stream.TwitchStreamId, true);
                    await recordingController.StopRecordingAsync(stream.TwitchStreamId);
                }
            }

            await repo.DeleteAsync(channelId);
            await IOUtils.DeleteDirectoryWithRetriesAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
            _ = twitchSubscription.UnsubscribeChannelAsync(channel, default);
            return Results.NoContent();
        })
        .WithName(nameof(DeleteChannel))
        .WithTags("Channels")
        .WithSummary("Remove a channel from monitoring")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
}