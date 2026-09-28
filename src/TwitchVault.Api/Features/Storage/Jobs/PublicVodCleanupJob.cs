using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using Stream = TwitchVault.Api.Features.Streams.Stream;

namespace TwitchVault.Api.Features.Storage.Jobs;

[DisallowConcurrentExecution]
public sealed class PublicVodCleanupJob(
    IDataStore dataStore,
    ITwitchGqlClient gqlClient,
    IDateTimeProvider dateTimeProvider,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    IOptionsMonitor<VaultOptions> vaultOptions,
    ILogger<PublicVodCleanupJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.PublicVodCleanup).Enabled)
            return;

        var retentionDays = vaultOptions.CurrentValue.PublicVodRetentionDays;
        var cutoff = dateTimeProvider.DateTimeNow.AddDays(-retentionDays);

        var streams = await dataStore.QueryAsync(
            ctx => ctx.Streams
                .EligibleForVodPrune(cutoff)
                .Include(s => s.Channel)
                .ToListAsync(context.CancellationToken));

        if (streams.Count == 0)
            return;

        logger.LogInformation(
            "Checking {Count} stream(s) (retention: {Days} days, cutoff: {Cutoff}).",
            streams.Count, retentionDays, cutoff);

        foreach (var stream in streams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            await CheckAndMarkAsync(stream, context.CancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(2), context.CancellationToken);
        }
    }

    private async Task CheckAndMarkAsync(Stream stream, CancellationToken ct)
    {
        var accessibility = await gqlClient.GetVodAccessibilityAsync(stream.VodId!, ct);

        await dataStore.ExecuteAsync(() =>
        {
            stream.MarkVodChecked(dateTimeProvider.DateTimeNow);

            if (accessibility == VodAccessibility.Public)
            {
                stream.RequestDeletion();
                logger.LogInformation(
                    "Stream '{StreamId}' (VOD {VodId}): publicly accessible on Twitch — marked for deletion.",
                    stream.Id, stream.VodId);
            }
            else
            {
                logger.LogInformation(
                    "Stream '{StreamId}' (VOD {VodId}): accessibility status is {Accessibility} — keeping locally.",
                    stream.Id, stream.VodId, accessibility);
            }

            dataStore.Save(stream);
            return Task.CompletedTask;
        });
    }
}