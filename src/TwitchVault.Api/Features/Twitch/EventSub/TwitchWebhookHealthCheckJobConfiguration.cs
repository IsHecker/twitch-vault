using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Twitch.EventSub;

public sealed class TwitchWebhookHealthCheckJobConfiguration(IOptions<BackgroundJobsOptions> jobsOptions)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(TwitchWebhookHealthCheckJob).FullName!;
        options
            .AddJob<TwitchWebhookHealthCheckJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(jobsOptions.Value.GetJob(JobOptions.TwitchWebhookHealthCheck).RunInterval).RepeatForever()));
    }
}