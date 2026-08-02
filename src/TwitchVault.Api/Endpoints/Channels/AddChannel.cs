using System.Security.Claims;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Channels;

public class AddChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        // ── Regular user: ShouldRecord and QualityRank are fixed ───────────────
        app.MapPost("/api/channels", async (
            UserRequest request,
            ClaimsPrincipal principal,
            ITwitchGqlClient twitchGqlClient,
            TwitchSubscriptionService twitchSubscription,
            IChannelRepository channelRepo,
            IUserChannelRepository userChannelRepo) =>
        {
            var userId = principal.GetUserId();

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

            var channel = new Channel
            {
                Id = channelId,
                Name = request.ChannelName,
                QualityRank = 2,
                ShouldRecord = true,
                IsLive = false
            };

            await channelRepo.AddAsync(channel);
            // _ = twitchSubscription.AddChannelsAsync([channel], default);

            await userChannelRepo.AddAsync(new UserChannel(userId, channelId, DateTime.UtcNow));
            return Results.Created($"/api/channels/{channel.Id}", ChannelResponse.FromDomain(channel));
        })
        .RequireAuthorization()
        .WithName(nameof(AddChannel))
        .WithTags("Channels")
        .WithSummary("Add a channel to monitor (ShouldRecord=true, QualityRank=2 by default)")
        .Accepts<UserRequest>("application/json")
        .Produces<ChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

        // ── Admin: full control over ShouldRecord and QualityRank ─────────────
        app.MapPost("/api/admin/channels", async (
            AdminRequest request,
            ClaimsPrincipal principal,
            ITwitchGqlClient twitchGqlClient,
            TwitchSubscriptionService twitchSubscription,
            IChannelRepository channelRepo,
            IUserChannelRepository userChannelRepo) =>
        {
            var userId = principal.GetUserId();

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

            var channel = new Channel
            {
                Id = channelId,
                Name = request.ChannelName,
                QualityRank = request.QualityRank,
                ShouldRecord = request.ShouldRecord,
                IsLive = false
            };
            await channelRepo.AddAsync(channel);
            // if (request.ShouldRecord)
            //     _ = twitchSubscription.AddChannelsAsync([channel], default);

            await userChannelRepo.AddAsync(new UserChannel(userId, channelId, DateTime.UtcNow));
            return Results.Created($"/api/channels/{channel.Id}", ChannelResponse.FromDomain(channel));
        })
        .RequireAuthorization("Admin")
        .WithName("AdminAddChannel")
        .WithTags("Admin")
        .WithSummary("[Admin] Add a channel with full control over ShouldRecord and QualityRank")
        .Accepts<AdminRequest>("application/json")
        .Produces<ChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    internal record struct UserRequest(string ChannelName);
    internal record struct AdminRequest(string ChannelName, int QualityRank, bool ShouldRecord);
}