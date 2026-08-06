using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Backblaze;

public sealed class BackblazeUploadJobConfiguration(SettingsService settingsService) : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(BackblazeUploadJob).FullName!;
        options
            .AddJob<BackblazeUploadJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(settingsService.Settings.BackgroundJobs["BackblazeUpload"].RunInterval).RepeatForever()));
    }
}