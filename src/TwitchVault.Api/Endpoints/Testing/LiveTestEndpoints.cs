using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence.Extensions;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Endpoints.Testing;

public class LiveTestEndpoints : IDevOnlyEndpoint
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
            IDataStore dataStore,
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
                // Skip if already in database (real channel) — do not touch or link real channels
                var existingChannel = await dataStore.QueryAsync<Channel, Channel?>(
                    q => q.FirstOrDefaultAsync(c => c.Id == twitchUserId));

                if (existingChannel is not null)
                {
                    skipped.Add($"{userLogin} (existing real channel, ignored)");
                    await Task.Delay(TimeSpan.FromSeconds(request.DelaySeconds));
                    continue;
                }

                // Brand-new channel — mirror AddChannel exactly
                var channel = Channel.Create(twitchUserId, userLogin, request.QualityRank, request.IsArchived);

                try
                {
                    await dataStore.ExecuteAsync(async () =>
                    {
                        await dataStore.AddAsync(channel);
                        await dataStore.AddAsync(UserChannel.Create(userId, twitchUserId, DateTime.UtcNow));
                        return Task.CompletedTask;
                    });

                    if (!request.IsArchived)
                    {
                        await recordingOrchestrator.TryStartRecordingAsync(channel.Id, channel.Name);
                    }

                    session.Track(twitchUserId, userLogin, isNewChannel: true);
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
        // POST /testing/live/finish-all                                       //
        // Stops and finalizes active recordings belonging ONLY to the        //
        // in-memory live-test session channels.                              //
        // ------------------------------------------------------------------ //
        group.MapPost("/finish-all", async (
            IRecordingOrchestrator recordingOrchestrator,
            IDataStore dataStore,
            LiveTestSession session) =>
        {
            if (!session.HasActiveSession)
            {
                return Results.Ok(new
                {
                    Finished = Array.Empty<string>(),
                    Count = 0
                });
            }

            var testChannelIds = session.ChannelIds;
            var finishedChannelIds = await recordingOrchestrator.FinishAllRecordingsAsync(testChannelIds);

            var finishedNames = new List<string>();
            foreach (var id in finishedChannelIds)
            {
                var name = session.GetName(id);
                if (name == null)
                {
                    var channel = await dataStore.QueryAsync<Channel, Channel?>(
                        q => q.FirstOrDefaultAsync(c => c.Id == id));
                    name = channel?.Name ?? id;
                }
                finishedNames.Add(name);
            }

            return Results.Ok(new
            {
                Finished = finishedNames,
                finishedNames.Count
            });
        })
        .WithName("LiveTestFinishAll")
        .WithSummary("[Admin] Stop active recordings for test session channels and finalize them with Finished status.");

        // ------------------------------------------------------------------ //
        // DELETE /testing/live/streams                                        //
        // Deletes cloud segments, local stream files, and DB stream records   //
        // ONLY for streams belonging to test-created channels.               //
        // ------------------------------------------------------------------ //
        group.MapDelete("/streams", async (
            IDataStore dataStore,
            IStreamService streamService,
            LiveTestSession session,
            ILogger<LiveTestEndpoints> logger) =>
        {
            if (!session.HasActiveSession)
                return Results.BadRequest("No active live-test session. Call /start first.");

            var createdChannels = session.GetCreatedChannels();
            var deletedStreams = new List<string>();
            var failedStreams = new List<string>();

            foreach (var (channelId, channelName) in createdChannels)
            {
                var streams = await dataStore.QueryAsync<Domain.Stream, List<Domain.Stream>>(
                    q => q.ForChannel(channelId).ToListAsync());

                foreach (var stream in streams)
                {
                    try
                    {
                        var result = await streamService.DeleteStreamAsync(stream.Id);
                        if (result.IsSuccess)
                            deletedStreams.Add($"{channelName} (Stream {stream.Id})");
                        else
                            failedStreams.Add($"{channelName} (Stream {stream.Id}): DeleteStreamAsync returned false");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to delete test stream {StreamId} for channel {Channel}", stream.Id, channelName);
                        failedStreams.Add($"{channelName} (Stream {stream.Id}): {ex.Message}");
                    }
                }
            }

            return Results.Ok(new
            {
                DeletedStreams = deletedStreams,
                FailedStreams = failedStreams,
                deletedStreams.Count
            });
        })
        .WithName("LiveTestDeleteStreams")
        .WithSummary("[Admin] Delete cloud segments and local stream folders ONLY for test-created channels.");

        // ------------------------------------------------------------------ //
        // DELETE /testing/live/channels                                       //
        // Deletes test-created channels from DB & disk, and unlinks           //
        // pre-existing authentic channels without touching their data.       //
        // ------------------------------------------------------------------ //
        group.MapDelete("/channels", async (
            IDataStore dataStore,
            IStreamService streamService,
            IRecordingOrchestrator recordingOrchestrator,
            IOptions<PathsOptions> pathsOptions,
            LiveTestSession session,
            ILogger<LiveTestEndpoints> logger) =>
        {
            if (!session.HasActiveSession)
                return Results.BadRequest("No active live-test session. Call /start first.");

            var sessionUserId = session.SessionUserId;
            var unlinkedChannels = new List<string>();
            var deletedChannels = new List<string>();
            var failedChannels = new List<string>();

            foreach (var (channelId, channelName) in session.GetCreatedChannels())
            {
                try
                {
                    var channel = await dataStore.QueryAsync<Channel, Channel?>(
                        q => q.FirstOrDefaultAsync(c => c.Id == channelId));

                    if (channel is null)
                    {
                        await dataStore.QueryAsync<UserChannel, int>(
                            q => q.Where(uc => uc.UserId == sessionUserId && uc.ChannelId == channelId).ExecuteDeleteAsync());
                        deletedChannels.Add($"{channelName} (already removed from DB)");
                        continue;
                    }

                    await dataStore.QueryAsync<UserChannel, int>(
                        q => q.Where(uc => uc.UserId == sessionUserId && uc.ChannelId == channelId).ExecuteDeleteAsync());

                    var remainingUsers = await dataStore.QueryAsync<UserChannel, int>(
                        q => q.CountAsync(uc => uc.ChannelId == channelId));

                    if (remainingUsers > 0)
                    {
                        unlinkedChannels.Add($"{channelName} (user link removed; {remainingUsers} watcher(s) remain)");
                        continue;
                    }

                    await dataStore.QueryAsync<Channel, int>(
                        q => q.Where(c => c.Id == channelId).ExecuteDeleteAsync());

                    var rootChannelDir = Path.Combine(pathsOptions.Value.Streams, channel.Name);
                    await Common.IOUtils.DeleteDirectoryWithRetriesAsync(rootChannelDir);

                    deletedChannels.Add(channelName);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete test channel {Channel}", channelName);
                    failedChannels.Add($"{channelName}: {ex.Message}");
                }
            }

            session.Clear();

            return Results.Ok(new
            {
                DeletedChannels = deletedChannels,
                UnlinkedChannels = unlinkedChannels,
                FailedChannels = failedChannels,
                SessionCleared = true
            });
        })
        .WithName("LiveTestDeleteChannels")
        .WithSummary("[Admin] Delete test-created channels (DB and root folders) and unlink authentic channels.");
    }

    internal record StartRequest(
        int Count,
        int DelaySeconds,
        int? MinViewers,
        int? MaxViewers,
        int MaxPages,
        int QualityRank,
        string Language,
        bool IsArchived);
}