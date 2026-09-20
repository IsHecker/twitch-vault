using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Recording;

namespace TwitchVault.Api.Endpoints.Channels;

public class SetArchiveStatus : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/archive", async (
            string channelId,
            Request request,
            IChannelService channelService,
            CancellationToken ct) =>
        {
            var result = await channelService.SetArchiveStatusAsync(channelId, request.Archive, ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(SetArchiveStatus))
        .WithTags("Channels")
        .WithSummary("[Admin] Enable or disable recording for a channel");

    internal record struct Request(bool Archive);
}