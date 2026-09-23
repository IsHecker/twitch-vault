namespace TwitchVault.Api.Features.Channels.Endpoints;

public class UnbanChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapDelete("/api/channels/banned/{channelName}", async (
            string channelName,
            IChannelBanService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.UnbanChannelAsync(channelName, ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization("Admin")
        .WithName("AdminUnbanChannel")
        .WithTags("Admin")
        .WithSummary("[Admin] Unban a previously banned channel")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
}