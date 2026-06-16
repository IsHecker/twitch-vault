using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Endpoints.Channels;

public class AddChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/channels", async (
            Request request,
            ITwitchGqlClient twitchGqlClient,
            TwitchSubscriptionService twitchSubscription,
            ChannelRepository repo) =>
        {
            var channelId = await twitchGqlClient.GetChannelIdAsync(request.ChannelName, default);
            if (string.IsNullOrWhiteSpace(channelId))
                return Results.NotFound("Channel doesn't exist.");

            var channel = new Channel
            {
                ChannelId = channelId,
                Name = request.ChannelName,
                QualityRank = request.QualityRank,
                ShouldRecord = request.ShouldRecord,
                IsLive = false
            };

            if (request.ShouldRecord)
                _ = twitchSubscription.SubscribeChannelAsync(channel, default);

            await repo.AddAsync(channel);
            return Results.Created($"/api/channels/{channel.ChannelId}", channel);
        })
        .WithName(nameof(AddChannel))
        .WithTags("Channels")
        .WithSummary("Add a channel to monitor")
        .Accepts<Channel>("application/json")
        .Produces<Channel>(StatusCodes.Status201Created);

    internal record struct Request(string ChannelName, int QualityRank, bool ShouldRecord);
}