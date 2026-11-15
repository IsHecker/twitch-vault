namespace TwitchVault.Api.Features.Recording.Storyboard;

public interface IStoryboardManager : IAsyncDisposable
{
    void Attach(string streamDirectory);
    Task ProcessSegmentAsync(string filePath, float durationSeconds, CancellationToken cancellationToken = default);
    Task ProcessKeyframeAsync(System.IO.Stream segmentStream, string extension, CancellationToken cancellationToken = default);
    Task FinalizeAsync(double totalDurationSeconds, CancellationToken cancellationToken = default);
    Task CompileAsync(Streams.Stream stream, CancellationToken cancellationToken = default);
}