namespace TwitchVault.Api.Features.Channels.Endpoints;

public class SubscribeChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/users/me/channels", async (
            Request request,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.SubscribeToChannelAsync(
                request.ChannelName,
                request.QualityRank,
                request.IsArchived,
                ct);

            return result.ToHttpResult(channel => Results.Created($"/api/users/me/channels/{channel.Id}", ChannelResponse.FromDomain(channel)));
        })
        .RequireAuthorization()
        .WithName(nameof(SubscribeChannel))
        .WithTags("Channels")
        .WithSummary("Subscribe to a channel to monitor.")
        .Accepts<Request>("application/json")
        .Produces<ChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    internal record struct Request(string ChannelName, int? QualityRank = null, bool? IsArchived = null);
}