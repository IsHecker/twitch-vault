using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Twitch.EventSub;

[DisallowConcurrentExecution]
public sealed class TwitchWebhookHealthCheckJob(
    TwitchHelixClient twitchHelixClient,
    TwitchSubscriptionService twitchSubscription,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    ILogger<TwitchWebhookHealthCheckJob> logger) : IJob
{
    private static readonly string TerminalFailureStatuses =
        "webhook_callback_verification_failed" +
        "&notification_failures_exceeded" +
        "&authorization_revoked" +
        "&moderator_removed" +
        "&user_removed" +
        "&version_removed" +
        "&beta_maintenance";

    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.TwitchWebhookHealthCheck).Enabled)
            return;

        var cancellationToken = context.CancellationToken;

        var deletedCount = 0;
        var deleteFailedCount = 0;
        var resubscribedCount = 0;

        try
        {
            var brokenSubscriptions = await twitchHelixClient
                .GetEventSubSubscriptionsAsync(TerminalFailureStatuses, cancellationToken)
                .ToListAsync(cancellationToken);

            if (brokenSubscriptions.Count == 0)
            {
                logger.LogDebug("All Webhook subscriptions are healthy.");
                return;
            }

            foreach (var sub in brokenSubscriptions)
            {
                var isDeleted = await twitchSubscription.RemoveChannelEventAsync(sub.Condition.BroadcasterUserId, sub.Type, cancellationToken);

                if (isDeleted == true)
                    deletedCount++;
                else if (isDeleted == false)
                    deleteFailedCount++;

                var isCreated = await twitchSubscription.AddChannelEventAsync(
                    sub.Condition.BroadcasterUserId,
                    sub.Type,
                    sub.Version,
                    cancellationToken);

                if (isCreated == true)
                    resubscribedCount++;
            }

            logger.LogInformation(
                "Webhook health fix applied: {Deleted} removed, {Failed} failed to delete, {Resubscribed} channel(s) resubscribed.",
                deletedCount, deleteFailedCount, resubscribedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fix EventSub webhook subscriptions during health check.");
        }
    }
}