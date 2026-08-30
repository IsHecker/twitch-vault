using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Persistence.Extensions;
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
            IRecordingOrchestrator recordingOrchestrator,
            AppDbContext db,
            IOptions<PathsOptions> pathsOptions,
            IWebHostEnvironment env) =>
        {
            var userId = principal.GetUserId();

            var userChannel = await db.UserChannels.FirstOrDefaultAsync(uc => uc.UserId == userId && uc.ChannelId == channelId);
            if (userChannel is null)
                return Results.NotFound();

            var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId);
            if (channel is null)
                return Results.NotFound();

            db.UserChannels.Remove(userChannel);
            await db.SaveChangesAsync();

            var remainingUserCount = await db.UserChannels.CountAsync(uc => uc.ChannelId == channelId);

            if (remainingUserCount > 0)
                return Results.NoContent();

            if (channel.IsLive)
            {
                var liveStream = await db.Streams
                    .ForChannel(channelId)
                    .OrderByDescending(s => s.StartedAt)
                    .FirstAsync();

                await recordingOrchestrator.StopRecordingAsync(liveStream.Id);
            }

            db.Channels.Remove(channel);
            await IOUtils.DeleteDirectoryWithRetriesAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
            await twitchSubscription.RemoveChannelAsync(channel, default);
            await db.SaveChangesAsync();

            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName(nameof(DeleteChannel))
        .WithTags("Channels")
        .WithSummary("Remove a channel from your monitoring list. Deletes the channel entirely if you are the last user watching it.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
}