using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Storage.Jobs;

public sealed class PublicVodCleanupJobConfiguration(IOptions<BackgroundJobsOptions> jobsOptions)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        var job = jobsOptions.Value.GetJob(JobOptions.PublicVodCleanup);
        string jobName = typeof(PublicVodCleanupJob).FullName!;

        options
            .AddJob<PublicVodCleanupJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
            {
                var trigger = configure.ForJob(jobName).StartNow();
                if (job.IsCron)
                {
                    trigger.WithCronSchedule(job.CronExpression!);
                }
                else
                {
                    trigger.WithSimpleSchedule(schedule =>
                        schedule.WithInterval(job.RunInterval).RepeatForever());
                }
            });
    }
}