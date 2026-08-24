using System.Security.Claims;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Domain;
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
            IChannelRepository channelRepo,
            IRecordingOrchestrator recordingOrchestrator,
            IUserChannelRepository userChannelRepo) =>
        {
            var userId = principal.GetUserId();
            var isAdmin = principal.IsInRole("Admin");

            var channelId = await twitchGqlClient.GetChannelIdAsync(request.ChannelName, default);
            if (string.IsNullOrWhiteSpace(channelId))
                return Results.NotFound("Channel doesn't exist on Twitch.");

            if (await userChannelRepo.ExistsAsync(userId, channelId))
                return Results.Conflict("You are already monitoring this channel.");

            var existingChannel = await channelRepo.GetByIdAsync(channelId);
            if (existingChannel is not null)
            {
                await userChannelRepo.AddAsync(new UserChannel(userId, channelId, DateTime.UtcNow));
                return Results.Created($"/api/channels/{existingChannel.Id}", ChannelResponse.FromDomain(existingChannel));
            }

            var qualityRank = isAdmin ? (request.QualityRank ?? 2) : 2;
            var shouldRecord = !isAdmin || (request.ShouldRecord ?? true);

            var channel = Channel.Create(channelId, request.ChannelName, qualityRank, shouldRecord);

            await channelRepo.AddAsync(channel);
            if (shouldRecord)
            {
                await twitchSubscription.AddChannelsAsync([channel], default);

                // TODO: delete
                await recordingOrchestrator.HandleStreamOnlineAsync(channel.Id, channel.Name);
            }

            await userChannelRepo.AddAsync(new UserChannel(userId, channelId, DateTime.UtcNow));

            return Results.Created($"/api/channels/{channel.Id}", ChannelResponse.FromDomain(channel));
        })
        .RequireAuthorization()
        .WithName(nameof(AddChannel))
        .WithTags("Channels")
        .WithSummary("Add a channel to monitor. Non-admin requests default to QualityRank=2 and ShouldRecord=true.")
        .Accepts<Request>("application/json")
        .Produces<ChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    internal record struct Request(string ChannelName, int? QualityRank = null, bool? ShouldRecord = null);
}