using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
namespace TwitchVault.Api.Endpoints.Channels;


public class GetAllChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/channels", async (IChannelRepository repo) => Results.Ok(await repo.GetAllAsync()))
            .WithName(nameof(GetAllChannels))
            .WithTags("Channels")
            .WithSummary("Get all monitored channels")
            .Produces<List<Channel>>();
}
