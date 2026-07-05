using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Recording;

public interface IStreamRecorderFactory
{
    Task<IStreamRecorder> CreateAsync(
        Domain.Stream stream,
        Channel channel,
        CancellationToken cancellationToken);
}

public class StreamRecorderFactory(
    IDateTimeProvider dateTimeProvider,
    IWebHostEnvironment environment,
    IServiceProvider serviceProvider,
    IFileSystem fileSystem) : IStreamRecorderFactory
{
    public async Task<IStreamRecorder> CreateAsync(
        Domain.Stream stream,
        Channel channel,
        CancellationToken cancellationToken)
    {
        stream.Folder.EnsureDirectoryExists(environment.ContentRootPath);

        var playlist = await HlsPlaylist.LoadOrCreateAsync(
            stream.Folder.RelativePath,
            dateTimeProvider,
            fileSystem,
            cancellationToken);

        var recorder = ActivatorUtilities.CreateInstance<StreamRecorder>(
            serviceProvider,
            playlist,
            cancellationToken);

        return recorder;
    }
}