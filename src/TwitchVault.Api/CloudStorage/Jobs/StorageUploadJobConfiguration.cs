using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.CloudStorage.Jobs;

public sealed class StorageUploadJobConfiguration : IConfigureOptions<QuartzOptions>
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
                        schedule.WithInterval(TimeSpan.FromMinutes(2)).RepeatForever()));
    }
}