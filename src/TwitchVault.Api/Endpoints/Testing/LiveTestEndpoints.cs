using System.Security.Claims;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Endpoints.Testing;

public class LiveTestEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/testing/live")
            .WithTags("Testing")
            .RequireAuthorization("Admin");

        group.MapPost("/start", async (
            StartRequest request,
            ClaimsPrincipal principal,
            TwitchHelixClient twitchHelixClient,
            IChannelRepository channelRepo,
            IUserChannelRepository userChannelRepo,
            IRecordingOrchestrator recordingOrchestrator,
            LiveTestSession session) =>
        {
            var userId = principal.GetUserId();

            var liveStreams = await twitchHelixClient.GetLiveStreamsAsync(
                count: request.Count,
                language: request.Language,
                minViewers: request.MinViewers ?? 0,
                maxViewers: request.MaxViewers ?? int.MaxValue,
                maxPages: request.MaxPages,
                cancellationToken: CancellationToken.None);

            if (liveStreams.Count == 0)
                return Results.Problem("Twitch Helix returned no live streams matching the criteria. Check filters or API credentials.");

            session.BeginSession(userId);

            var added = new List<object>();
            var skipped = new List<string>();
            var failed = new List<string>();

            foreach (var (twitchUserId, userLogin, viewerCount) in liveStreams)
            {
                // Skip if already being monitored by this user — exact AddChannel behaviour
                if (await userChannelRepo.ExistsAsync(userId, twitchUserId))
                {
                    skipped.Add($"{userLogin} (already monitored by you)");
                    await Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds));
                    continue;
                }

                var existingChannel = await channelRepo.GetByIdAsync(twitchUserId);
                if (existingChannel is not null)
                {
                    // Channel already tracked by another user — just link this user
                    await userChannelRepo.AddAsync(new UserChannel(userId, twitchUserId, DateTime.UtcNow));
                    session.Track(twitchUserId, userLogin);
                    skipped.Add($"{userLogin} (already tracked, linked user)");
                    await Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds));
                    continue;
                }

                // Brand-new channel — mirror AddChannel exactly
                var channel = Channel.Create(twitchUserId, userLogin, request.QualityRank, request.ShouldRecord);

                try
                {
                    await channelRepo.AddAsync(channel);

                    if (request.ShouldRecord)
                    {
                        await recordingOrchestrator.HandleStreamOnlineAsync(channel.Id, channel.Name);
                    }

                    await userChannelRepo.AddAsync(new UserChannel(userId, twitchUserId, DateTime.UtcNow));

                    session.Track(twitchUserId, userLogin);
                    added.Add(new { Channel = userLogin, Viewers = viewerCount, ChannelId = twitchUserId });
                }
                catch (Exception ex)
                {
                    failed.Add($"{userLogin}: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds));
            }

            return Results.Ok(new
            {
                Added = added,
                Skipped = skipped,
                Failed = failed,
                SessionChannelCount = session.ChannelIds.Count
            });
        })
        .WithName("LiveTestStart")
        .WithSummary("[Admin] Fetch N live streams from Twitch and start recording them as a test session.")
        .Accepts<StartRequest>("application/json");

        // ------------------------------------------------------------------ //
        // POST /testing/live/stop-all                                          //
        // Stops active recordings for all channels tracked by this session.   //
        // Marks streams for deletion and calls StopRecordingAsync — the same  //
        // sequence DeleteChannel uses when a live channel is removed.          //
        // Does NOT delete channels from the DB; use delete-all for that.       //
        // ------------------------------------------------------------------ //
        group.MapPost("/stop-all", async (
            IStreamRepository streamRepository,
            IRecordingOrchestrator recordingOrchestrator,
            LiveTestSession session) =>
        {
            if (!session.HasActiveSession)
                return Results.BadRequest("No active live-test session. Call /start first.");

            var snapshot = session.GetSnapshot();
            var stopped = new List<string>();
            var skipped = new List<string>();

            foreach (var (channelId, channelName) in snapshot)
            {
                await recordingOrchestrator.ToggleStreamDeletionAsync(channelId, true);
                await recordingOrchestrator.StopRecordingAsync(channelId);
                stopped.Add(channelName);
            }

            return Results.Ok(new { Stopped = stopped, Skipped = skipped });
        })
        .WithName("LiveTestStopAll")
        .WithSummary("[Admin] Stop all active recordings started by the current live-test session.");

        // ------------------------------------------------------------------ //
        // DELETE /testing/live/delete-all                                      //
        // Full teardown: for every channel tracked by this session, runs the  //
        // exact DeleteChannel logic (stop recording if live, delete from DB,  //
        // UserChannel link). Clears the session state when done.              //
        // ------------------------------------------------------------------ //
        group.MapDelete("/delete-all", async (
            ClaimsPrincipal principal,
            IChannelRepository channelRepo,
            IUserChannelRepository userChannelRepo,
            IStreamRepository streamRepository,
            IRecordingOrchestrator recordingOrchestrator,
            IOptions<PathsOptions> pathsOptions,
            LiveTestSession session) =>
        {
            if (!session.HasActiveSession)
                return Results.BadRequest("No active live-test session. Call /start first.");

            // Use the userId stored at session start, not the current caller's id,
            // so delete-all is idempotent even if called by a different admin token.
            var sessionUserId = session.SessionUserId;
            var snapshot = session.GetSnapshot();
            var deleted = new List<string>();
            var failed = new List<string>();

            foreach (var (channelId, channelName) in snapshot)
            {
                try
                {
                    var channel = await channelRepo.GetByIdAsync(channelId);
                    if (channel is null)
                    {
                        // Might have been partially cleaned already; still remove the UserChannel link if it exists
                        await userChannelRepo.RemoveAsync(sessionUserId, channelId);
                        deleted.Add($"{channelName} (channel row missing, cleaned link only)");
                        continue;
                    }

                    // 1. Remove UserChannel link for this session's user
                    await userChannelRepo.RemoveAsync(sessionUserId, channelId);

                    // 2. Check how many other users still watch this channel
                    var remainingUsers = await userChannelRepo.GetUserCountForChannelAsync(channelId);
                    if (remainingUsers > 0)
                    {
                        // Other users still watching — do not touch the channel itself
                        deleted.Add($"{channelName} (user link removed; {remainingUsers} other watcher(s) remain)");
                        continue;
                    }

                    // 3. If live, stop recording first — exact DeleteChannel sequence
                    if (channel.IsLive)
                    {
                        await recordingOrchestrator.ToggleStreamDeletionAsync(channel.Id, true);
                        await recordingOrchestrator.StopRecordingAsync(channel.Id);
                    }

                    await channelRepo.DeleteAsync(channelId);

                    deleted.Add(channelName);
                }
                catch (Exception ex)
                {
                    failed.Add($"{channelName}: {ex.Message}");
                }
            }

            session.Clear();

            return Results.Ok(new
            {
                Deleted = deleted,
                Failed = failed,
                SessionCleared = true
            });
        })
        .WithName("LiveTestDeleteAll")
        .WithSummary("[Admin] Full teardown of all channels added by the current live-test session.");
    }

    /// <summary>Options for the /start endpoint — all have sensible defaults so the UI can omit any field.</summary>
    internal record StartRequest(
        int Count,
        int DelaySeconds,
        int? MinViewers,
        int? MaxViewers,
        int MaxPages,
        int QualityRank,
        string Language,
        bool ShouldRecord);
}