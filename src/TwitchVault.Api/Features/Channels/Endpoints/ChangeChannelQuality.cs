namespace TwitchVault.Api.Features.Channels.Endpoints;

public class ChangeChannelQuality : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPut("/api/channels/{channelId}/quality", async (
            string channelId,
            Request request,
            IChannelService channelService,
            IRecordingOrchestrator recordingOrchestrator,
            CancellationToken ct) =>
        {
            var result = await channelService.ChangeChannelQualityAsync(channelId, request.QualityRank, ct);
            return result.ToHttpResult();
        })
        .RequireAuthorization("Admin")
        .WithName(nameof(ChangeChannelQuality))
        .WithTags("Channels")
        .WithSummary("[Admin] Update the quality rank of a channel")
        .Produces<ChannelResponse>();

    internal record struct Request(int QualityRank);
}