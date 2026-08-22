using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.ChannelMonitor;

public sealed class ChannelMonitorJobConfiguration(IOptions<BackgroundJobsOptions> jobsOptions)
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
                        schedule.WithInterval(jobsOptions.Value.GetJob(JobOptions.ChannelMonitor).RunInterval).RepeatForever()));
    }
}