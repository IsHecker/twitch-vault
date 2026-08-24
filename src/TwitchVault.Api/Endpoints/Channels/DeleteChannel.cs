using System.Security.Claims;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Channels;

public class DeleteChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/channels/{channelId}", async (
            string channelId,
            ClaimsPrincipal principal,
            TwitchSubscriptionService twitchSubscription,
            IChannelRepository channelRepo,
            IUserChannelRepository userChannelRepo,
            IRecordingOrchestrator recordingOrchestrator,
            IStreamRepository streamRepository,
            IOptions<PathsOptions> pathsOptions,
            IWebHostEnvironment env) =>
        {
            var userId = principal.GetUserId();

            if (!await userChannelRepo.ExistsAsync(userId, channelId))
                return Results.NotFound();

            var channel = await channelRepo.GetByIdAsync(channelId);
            if (channel is null)
                return Results.NotFound();

            await userChannelRepo.RemoveAsync(userId, channelId);

            var remainingUserCount = await userChannelRepo.GetUserCountForChannelAsync(channelId);

            if (remainingUserCount > 0)
                return Results.NoContent();

            if (channel.IsLive)
            {
                var streams = await streamRepository.ListByChannelIdAsync(channelId);
                var liveStream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();
                if (liveStream is not null)
                {
                    await recordingOrchestrator.ToggleStreamDeletionAsync(liveStream.TwitchStreamId, true);
                    await recordingOrchestrator.StopRecordingAsync(liveStream.TwitchStreamId);
                }
            }

            await channelRepo.DeleteAsync(channelId);
            await IOUtils.DeleteDirectoryWithRetriesAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
            await twitchSubscription.RemoveChannelAsync(channel, default);

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName(nameof(DeleteChannel))
        .WithTags("Channels")
        .WithSummary("Remove a channel from your monitoring list. Deletes the channel entirely if you are the last user watching it.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
}