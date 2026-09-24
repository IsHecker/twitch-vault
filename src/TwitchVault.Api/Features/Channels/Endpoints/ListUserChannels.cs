namespace TwitchVault.Api.Features.Channels.Endpoints;

public class ListUserChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/me/channels", async (
            ICurrentUser user,
            [AsParameters] Pagination pagination,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var response = await channelService.GetChannelsForUserAsync(user.Id, pagination, ct);
            return response.ToHttpResult(Results.Ok);
        })
        .RequireAuthorization()
        .WithName(nameof(ListUserChannels))
        .WithTags("Channels")
        .WithSummary("List channels subscribed to by the currently authenticated user")
        .Produces<PagedResponse<ChannelResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);
}