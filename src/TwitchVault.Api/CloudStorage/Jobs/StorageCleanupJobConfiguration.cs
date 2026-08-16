using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.CloudStorage.Jobs;

public sealed class StorageCleanupJobConfiguration(SettingsService settingsService)
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
                        schedule.WithInterval(settingsService.Settings.BackgroundJobs[JobOptions.StorageCleanup].RunInterval).RepeatForever()));
    }
}