namespace TwitchVault.Api.Features.Channels.Endpoints;

public class ListAllChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels", async (
            [AsParameters] Pagination pagination,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var paged = await channelService.GetAllChannelsAsync(pagination, ct);
            return Results.Ok(paged);
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetAllChannels")
        .WithTags("Admin")
        .WithSummary("[Admin] List all channels in the system across all users")
        .Produces<PagedResponse<ChannelResponse>>();
}