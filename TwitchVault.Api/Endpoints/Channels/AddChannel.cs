using TwitchVault.Api.Models;
using TwitchVault.Api.Repositories;

namespace TwitchVault.Api.Endpoints.Channels;

public class AddChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/channels", async (Request request, ChannelRepository repo) =>
        {
            var channel = new Channel
            {
                Name = request.ChannelName,
                QualityRank = request.QualityRank,
                IsLive = false
            };

            await repo.AddAsync(channel);
            return Results.Created($"/api/channels/{channel.ChannelId}", channel);
        })
        .WithName(nameof(AddChannel))
        .WithTags("Channels")
        .WithSummary("Add a channel to monitor")
        .Accepts<Channel>("application/json")
        .Produces<Channel>(StatusCodes.Status201Created);

    internal record struct Request(string ChannelName, int QualityRank);
}