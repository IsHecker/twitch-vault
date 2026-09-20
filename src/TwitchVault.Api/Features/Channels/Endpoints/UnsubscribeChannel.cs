using System.Security.Claims;

namespace TwitchVault.Api.Features.Channels.Endpoints;

public class UnsubscribeChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/users/me/channels/{channelId}", async (
            string channelId,
            ClaimsPrincipal principal,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.UnsubscribeChannelAsync(principal.GetUserId(), channelId, ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization()
        .WithName(nameof(UnsubscribeChannel))
        .WithTags("Channels")
        .WithSummary("Unsubscribe from a channel. Removes the channel and its streams if no other users are subscribed to it.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound);
}