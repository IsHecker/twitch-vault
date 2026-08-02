using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Admin;

public class GetAllChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/channels", async (IChannelRepository channelRepo) =>
        {
            var channels = await channelRepo.GetAllAsync();
            return Results.Ok(channels.Select(Channels.ChannelResponse.FromDomain).ToList());
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetAllChannels")
        .WithTags("Admin")
        .WithSummary("[Admin] List all channels in the system across all users")
        .Produces<List<Channels.ChannelResponse>>();
}