using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.CloudStorage.Jobs;

public sealed class StorageUploadJobConfiguration(IOptions<BackgroundJobsOptions> jobsOptions)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(StorageUploadJob).FullName!;
        options
            .AddJob<StorageUploadJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(jobsOptions.Value.GetJob(JobOptions.StorageUpload).RunInterval).RepeatForever()));
    }
}