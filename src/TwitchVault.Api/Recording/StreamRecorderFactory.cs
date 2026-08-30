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
    IWebHostEnvironment env,
    IServiceProvider serviceProvider,
    IFileSystem fileSystem) : IStreamRecorderFactory
{
    public async Task<IStreamRecorder> CreateAsync(
        Domain.Stream stream,
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
            cancellationToken);

        return recorder;
    }
}