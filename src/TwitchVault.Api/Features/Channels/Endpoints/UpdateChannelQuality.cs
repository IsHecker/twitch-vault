using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Endpoints.Channels;

public class UpdateChannelQuality : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/quality", async (
            string channelId,
            Request request,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.UpdateChannelQualityAsync(channelId, request.QualityRank, ct);
            return result.ToHttpResult(channel => Results.Ok(ChannelResponse.FromDomain(channel)));
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(UpdateChannelQuality))
        .WithTags("Channels")
        .WithSummary("[Admin] Update the quality rank of a channel")
        .Produces<ChannelResponse>();

    internal record struct Request(int QualityRank);
}