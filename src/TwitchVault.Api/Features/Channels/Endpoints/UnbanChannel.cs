namespace TwitchVault.Api.Features.Channels.Endpoints;

public class UnbanChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/admin/channels/banned/{channelId}", async (
            string channelId,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.UnbanChannelAsync(channelId, ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization("Admin")
        .WithName("AdminUnbanChannel")
        .WithTags("Admin")
        .WithSummary("[Admin] Unban a previously banned channel")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
}