using System.Security.Claims;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Endpoints.Channels;

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
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict);
    }

    internal record struct Request(string ChannelName, int? QualityRank = null, bool? IsArchived = null);
}