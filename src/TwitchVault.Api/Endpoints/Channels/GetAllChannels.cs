using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;

using TwitchVault.Api.Events;
namespace TwitchVault.Api.Endpoints.Channels;


public class GetAllChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels", async (ChannelRepository repo) => Results.Ok(await repo.GetAllAsync()))
            .WithName(nameof(GetAllChannels))
            .WithTags("Channels")
            .WithSummary("Get all monitored channels")
            .Produces<List<Channel>>();
}
