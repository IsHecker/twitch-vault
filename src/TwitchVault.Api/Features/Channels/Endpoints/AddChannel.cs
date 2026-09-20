using System.Security.Claims;

namespace TwitchVault.Api.Features.Channels.Endpoints;

public class AddChannel : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/channels", async (
            Request request,
            ClaimsPrincipal principal,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.AddChannelAsync(
                principal.GetUserId(),
                request.ChannelName,
                request.QualityRank,
                request.IsArchived,
                principal.IsInRole("Admin"),
                ct);

            return result.ToHttpResult(channel => Results.Created($"/api/channels/{channel.Id}", ChannelResponse.FromDomain(channel)));
        })
        .RequireAuthorization()
        .WithName(nameof(AddChannel))
        .WithTags("Channels")
        .WithSummary("Add a channel to monitor.")
        .Accepts<Request>("application/json")
        .Produces<ChannelResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    internal record struct Request(string ChannelName, int? QualityRank = null, bool? IsArchived = null);
}