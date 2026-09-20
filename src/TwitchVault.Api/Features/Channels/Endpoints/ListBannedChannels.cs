namespace TwitchVault.Api.Features.Channels.Endpoints;

public class ListBannedChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels/banned", async (
            [AsParameters] Pagination pagination,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var paged = await channelService.GetBannedChannelsAsync(pagination, ct);
            return Results.Ok(paged);
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetBannedChannels")
        .WithTags("Admin")
        .WithSummary("[Admin] List all banned channels")
        .Produces<PagedResponse<BannedChannelResponse>>();
}