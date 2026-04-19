using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.StoppedStreamCheck;

public sealed class StoppedStreamCheckJobConfiguration(SettingsService settingsService)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(StoppedStreamCheckJob).FullName!;

        options
            .AddJob<StoppedStreamCheckJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(settingsService.Settings.StoppedStreamCheck.RunInterval).RepeatForever()));
    }
}