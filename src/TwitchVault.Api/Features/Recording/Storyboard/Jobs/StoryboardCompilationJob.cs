using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace TwitchVault.Api.Features.Recording.Storyboard.Jobs;

[DisallowConcurrentExecution]
public sealed class StoryboardCompilationJob(
    AppDbContext dbContext,
    StoryboardCompiler storyboard,
    IOptionsMonitor<BackgroundJobsOptions> jobsOptions,
    ILogger<StoryboardCompilationJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        if (!jobsOptions.CurrentValue.GetJob(JobOptions.StoryboardCompilation).Enabled)
            return;

        var finalizingStreams = await dbContext.Streams
                .Where(s => s.Status == StreamStatus.Finalizing)
                .OrderByDescending(s => s.StartedAt)
                .ToListAsync(context.CancellationToken);

        if (finalizingStreams.Count == 0)
            return;

        foreach (var stream in finalizingStreams)
        {
            if (context.CancellationToken.IsCancellationRequested)
                break;

            try
            {
                await storyboard.CompileAsync(stream, context.CancellationToken);
                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to compile storyboard for stream '{StreamId}'. Will retry on next scheduled execution.", stream.Id);
            }
        }
    }
}