using System.Security.Claims;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Endpoints.Channels;

public class ListUserChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me/channels", async (
            ClaimsPrincipal principal,
            [AsParameters] Pagination pagination,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.GetChannelsForUserAsync(principal.GetUserId(), pagination, ct);
            return result.ToHttpResult(Results.Ok);
        })
        .RequireAuthorization()
        .WithName(nameof(ListUserChannels))
        .WithTags("Channels")
        .WithSummary("List channels subscribed to by the currently authenticated user")
        .Produces<PagedResponse<ChannelResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);
}