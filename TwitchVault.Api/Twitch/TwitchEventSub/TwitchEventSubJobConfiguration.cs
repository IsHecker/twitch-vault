using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Twitch.TwitchEventSub;

public sealed class TwitchEventSubJobConfiguration() : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(TwitchEventSubJob).FullName!;

        options
            .AddJob<TwitchEventSubJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever()));
    }
}