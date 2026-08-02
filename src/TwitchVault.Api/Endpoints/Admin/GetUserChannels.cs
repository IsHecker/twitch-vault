using TwitchVault.Api.Persistence;

namespace TwitchVault.Api.Endpoints.Admin;

public class GetUserChannels : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/admin/users/{userId:guid}/channels", async (
            Guid userId,
            IUserRepository userRepo,
            IUserChannelRepository userChannelRepo,
            IChannelRepository channelRepo) =>
        {
            var user = await userRepo.GetByIdAsync(userId);
            if (user is null)
                return Results.NotFound($"User '{userId}' not found.");

            var channelIds = await userChannelRepo.GetChannelIdsForUserAsync(userId);
            var channels = await channelRepo.GetByIdsAsync(channelIds);
            return Results.Ok(channels.Select(Channels.ChannelResponse.FromDomain).ToList());
        })
        .RequireAuthorization("Admin")
        .WithName("AdminGetUserChannels")
        .WithTags("Admin")
        .WithSummary("[Admin] List all channels belonging to a specific user")
        .Produces<List<Channels.ChannelResponse>>()
        .Produces(StatusCodes.Status404NotFound);
}