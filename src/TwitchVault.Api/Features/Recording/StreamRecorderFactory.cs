namespace TwitchVault.Api.Features.Recording;

public interface IStreamRecorderFactory
{
    Task<IStreamRecorder> CreateAsync(
        Streams.Stream stream,
        Channel channel,
        CancellationToken cancellationToken);
}

public class StreamRecorderFactory(
    IDateTimeProvider dateTimeProvider,
    IServiceProvider serviceProvider,
    IFileSystem fileSystem) : IStreamRecorderFactory
{
    public async Task<IStreamRecorder> CreateAsync(
        Streams.Stream stream,
        Channel channel,
        CancellationToken cancellationToken)
    {

        stream.Folder.EnsureDirectoryExists();

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