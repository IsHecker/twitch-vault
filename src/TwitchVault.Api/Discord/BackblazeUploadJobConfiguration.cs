using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Discord;

public sealed class DiscordUploadJobConfiguration(SettingsService settingsService) : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(DiscordUploadJob).FullName!;
        options
            .AddJob<DiscordUploadJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(settingsService.Settings.BackgroundJobs[JobOptions.DiscordUpload].RunInterval).RepeatForever()));
    }
}