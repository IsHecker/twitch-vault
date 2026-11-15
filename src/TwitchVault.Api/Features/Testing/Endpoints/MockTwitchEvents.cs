
namespace TwitchVault.Api.Features.Testing.Endpoints;

public class MockTwitchEvents : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/twitch").WithTags("Testing");
        group.MapPost("/online", async (
            string channelId,
            string channelName,
            IRecordingOrchestrator recordingOrchestrator) =>
        {
            await recordingOrchestrator.TryStartRecordingAsync(channelId, channelName);
            return Results.Ok($"Sent StreamOnlineEvent for {channelName}");
        });

        group.MapPost("/update", async (string channelId, string title, string categoryId, EventBus bus) =>
        {
            await bus.PublishAsync(channelId, new ChannelUpdateEvent(channelId, title, categoryId));
            return Results.Ok($"Sent ChannelUpdateEvent: {title} | {categoryId}");
        });

        group.MapPost("/clear-events", async (TwitchSubscriptionService service) =>
        {
            await service.ClearAllSubscriptionsAsync(default);
            return Results.Ok();
        });

        group.MapPost("/storyboard", async (string path, int duration, string dest, IStoryboardManager storyboard) =>
        {
            // var segs = new DirectoryInfo(path)
            //     .EnumerateFiles()
            //     .Where(f => !f.Name.Equals("init.mp4", StringComparison.OrdinalIgnoreCase)
            //             && Path.GetExtension(f.Name) is ".ts" or ".mp4")
            //     .ToList();

            // storyboard.Attach(dest);
            // foreach (var seg in segs)
            // {
            //     var stream = File.OpenRead(seg.FullName);
            //     await storyboard.ProcessKeyframeAsync(stream, Path.GetExtension(stream.Name));
            // }
            storyboard.Attach(Path.GetDirectoryName(path)!);
            var stream = File.OpenRead(path);
            await storyboard.ProcessKeyframeAsync(stream, Path.GetExtension(stream.Name));
            await storyboard.FinalizeAsync(duration);
            return Results.Ok();
        });
    }
}