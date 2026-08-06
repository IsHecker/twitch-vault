using Microsoft.Extensions.Options;
using Quartz;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Twitch.EventSub;

public sealed class TwitchWebhookHealthCheckJobConfiguration(SettingsService settingsService)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(TwitchWebhookHealthCheckJob).FullName!;
        options
            .AddJob<TwitchWebhookHealthCheckJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(settingsService.Settings.BackgroundJobs["TwitchWebhookHealthCheck"].RunInterval).RepeatForever()));
    }
}