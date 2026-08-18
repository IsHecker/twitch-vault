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
        const int MaxConsecutiveNetworkErrors = 5;
        var networkErrorDelay = TimeSpan.FromSeconds(2);

        stream.Folder.EnsureDirectoryExists(environment.ContentRootPath);

        var playlist = await HlsPlaylistWriter.LoadOrCreateAsync(
            stream.Folder.RelativePath,
            dateTimeProvider,
            fileSystem,
            cancellationToken);

        var retryPolicy = ActivatorUtilities.CreateInstance<TransientErrorRetryPolicy>(
            serviceProvider,
            MaxConsecutiveNetworkErrors,
            networkErrorDelay);

        var recorder = ActivatorUtilities.CreateInstance<StreamRecorder>(
            serviceProvider,
            playlist,
            retryPolicy,
            cancellationToken);

        return recorder;
    }
}