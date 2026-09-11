using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Channels;

public class AddChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/channels", async (
            Request request,
            ClaimsPrincipal principal,
            ITwitchGqlClient twitchGqlClient,
            TwitchSubscriptionService twitchSubscription,
            IRecordingOrchestrator recordingOrchestrator,
            AppDbContext db,
            IDateTimeProvider timeProvider) =>
        {
            var dateTimeNow = timeProvider.DateTimeNow;
            var userId = principal.GetUserId();
            var isAdmin = principal.IsInRole("Admin");

            var channelId = await twitchGqlClient.GetChannelIdAsync(request.ChannelName, default);
            if (string.IsNullOrWhiteSpace(channelId))
                return Results.NotFound("Channel doesn't exist on Twitch.");

            var alreadyMonitoring = await db.UserChannels
                .AnyAsync(uc => uc.UserId == userId && uc.ChannelId == channelId);

            if (alreadyMonitoring)
                return Results.Conflict("You are already subscribed to this channel.");

            db.UserChannels.Add(UserChannel.Create(userId, channelId, dateTimeNow));

            var existingChannel = await db.Channels.GetByIdAsync(channelId);
            if (existingChannel is not null)
            {
                await db.SaveChangesAsync();
                return Results.Created($"/api/channels/{existingChannel.Id}", ChannelResponse.FromDomain(existingChannel));
            }

            var qualityRank = isAdmin ? request.QualityRank!.Value : 2;
            var isArchived = isAdmin && request.IsArchived!.Value;

            var channel = Channel.Create(channelId, request.ChannelName, qualityRank, isArchived);

            db.Channels.Add(channel);
            await db.SaveChangesAsync();

            if (!isArchived)
            {
                // await twitchSubscription.AddChannelsAsync([channel], default);

                // TODO: delete
                await recordingOrchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
            }

            return Results.Created($"/api/channels/{channel.Id}", ChannelResponse.FromDomain(channel));
        })
        .RequireAuthorization()
        .WithName(nameof(AddChannel))
        .WithTags("Channels")
        .WithSummary("Add a channel to monitor.")
        .Accepts<Request>("application/json")
        .Produces<ChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    internal record struct Request(string ChannelName, int? QualityRank = null, bool? IsArchived = null);
}