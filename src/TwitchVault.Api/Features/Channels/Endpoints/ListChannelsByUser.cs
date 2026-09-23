namespace TwitchVault.Api.Features.Channels.Endpoints;

public class ListChannelsByUser : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/users/{userId:guid}/channels", async (
            Guid userId,
            [AsParameters] Pagination pagination,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.GetChannelsForUserAsync(userId, pagination, ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(ListChannelsByUser))
        .WithTags("Channels")
        .WithSummary("List all channels belonging to a specific user")
        .Produces<PagedResponse<ChannelResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status403Forbidden);
}