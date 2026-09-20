namespace TwitchVault.Api.Features.Channels.Endpoints;

public class BanChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/admin/channels/ban", async (
            Request request,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.BanChannelAsync(request.ChannelName, request.Reason, ct);
            return result.ToHttpResult(banned => Results.Created($"/api/admin/channels/banned/{banned.Id}", BannedChannelResponse.FromDomain(banned)));
        })
        .RequireAuthorization("Admin")
        .WithName("AdminBanChannel")
        .WithTags("Admin")
        .WithSummary("[Admin] Ban a channel from being subscribed to by regular users")
        .Accepts<Request>("application/json")
        .Produces<BannedChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);

    internal record Request(string ChannelName, string? Reason = null);
}