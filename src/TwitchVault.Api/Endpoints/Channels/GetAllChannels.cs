using System.Security.Claims;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Channels;

public class GetAllChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels", async (
            ClaimsPrincipal principal,
            IUserChannelRepository userChannelRepo,
            IChannelRepository channelRepo) =>
        {
            var userId = principal.GetUserId();
            var channelIds = await userChannelRepo.GetChannelIdsForUserAsync(userId);
            var channels = await channelRepo.GetByIdsAsync(channelIds);
            return Results.Ok(channels.Select(ChannelResponse.FromDomain).ToList());
        })
        .RequireAuthorization()
        .WithName(nameof(GetAllChannels))
        .WithTags("Channels")
        .WithSummary("Get all channels the authenticated user is monitoring")
        .Produces<List<ChannelResponse>>();
}