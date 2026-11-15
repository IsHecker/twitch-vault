using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Recording.Storyboard.Jobs;

public sealed class StoryboardCompilationJobConfiguration(IOptions<BackgroundJobsOptions> jobsOptions)
    : IConfigureOptions<QuartzOptions>
{
    public void Configure(QuartzOptions options)
    {
        string jobName = typeof(StoryboardCompilationJob).FullName!;
        var jobOpt = jobsOptions.Value.GetJob(JobOptions.StoryboardCompilation);
        var interval = jobOpt.RunInterval > TimeSpan.Zero ? jobOpt.RunInterval : TimeSpan.FromMinutes(1);

        options
            .AddJob<StoryboardCompilationJob>(configure => configure.WithIdentity(jobName))
            .AddTrigger(configure =>
                configure
                    .ForJob(jobName)
                    .StartNow()
                    .WithSimpleSchedule(schedule =>
                        schedule.WithInterval(interval).RepeatForever()));
    }
}