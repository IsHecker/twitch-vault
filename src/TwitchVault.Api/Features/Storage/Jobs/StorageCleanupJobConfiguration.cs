using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Storage.Jobs;

public sealed class StorageCleanupJobConfiguration(IOptions<BackgroundJobsOptions> jobsOptions)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(StorageCleanupJob).FullName!;
        options
            .AddJob<StorageCleanupJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(jobsOptions.Value.GetJob(JobOptions.StorageCleanup).RunInterval).RepeatForever()));
    }
}