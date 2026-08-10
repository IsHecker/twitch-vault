using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.ChannelMonitor;

public sealed class ChannelMonitorJobConfiguration(SettingsService settingsService)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(ChannelMonitorJob).FullName!;
        options
            .AddJob<ChannelMonitorJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(settingsService.Settings.BackgroundJobs[JobOptions.ChannelMonitor].RunInterval).RepeatForever()));
    }
}