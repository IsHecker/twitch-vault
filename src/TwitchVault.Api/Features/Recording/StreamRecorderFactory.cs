namespace TwitchVault.Api.Features.Recording;

public interface IStreamRecorderFactory
{
    Task<IStreamRecorder> CreateAsync(
        TwitchVault.Api.Features.Streams.Stream stream,
        Channel channel,
        CancellationToken cancellationToken);
}

public class StreamRecorderFactory(
    IDateTimeProvider dateTimeProvider,
    IWebHostEnvironment env,
    IServiceProvider serviceProvider,
    IFileSystem fileSystem) : IStreamRecorderFactory
{
    public async Task<IStreamRecorder> CreateAsync(
        TwitchVault.Api.Features.Streams.Stream stream,
        Channel channel,
        CancellationToken cancellationToken)
    {

        stream.Folder.EnsureDirectoryExists(env.ContentRootPath);

        var playlist = await HlsPlaylistWriter.LoadOrCreateAsync(
            stream.Folder.RelativePath,
            dateTimeProvider,
            fileSystem,
            cancellationToken);

        var recorder = ActivatorUtilities.CreateInstance<StreamRecorder>(
            serviceProvider,
            playlist,
            TimeProvider.System,
            cancellationToken);

        return recorder;
    }
}