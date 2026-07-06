using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IStreamFinalizer
{
    Task FinalizeAsync(
        Domain.Stream stream,
        Channel channel,
        ISegmentDownloader segmentDownloader,
        IHlsPlaylist hlsPlaylist,
        SessionEndReason reason);
}

public abstract record SessionEndReason
{
    public sealed record StreamEnded : SessionEndReason;
    public sealed record StreamStopped : SessionEndReason;
    public sealed record StreamError(Exception Ex) : SessionEndReason;
}

public sealed class StreamFinalizer(
    IStreamRepository streamRepository,
    IChannelRepository channelRepository,
    IStreamService streamService,
    ITwitchGqlClient twitchClient,
    IDateTimeProvider dateTimeProvider,
    IOptions<PathsOptions> pathsOptions,
    ILogger<StreamFinalizer> logger) : IStreamFinalizer
{
    public async Task FinalizeAsync(
        Domain.Stream stream,
        Channel channel,
        ISegmentDownloader segmentDownloader,
        IHlsPlaylist hlsPlaylist,
        SessionEndReason reason)
    {
        try
        {
            segmentDownloader.CloseSegment();
            await hlsPlaylist.FinalizeAsync();

            stream.SetThumbnailUrl(stream.Folder.GetThumbnailUrl(pathsOptions.Value.BaseUrl));
            await streamRepository.UpdateAsync(stream);

            if (stream.MarkForDeletion)
            {
                await streamService.DeleteStreamAsync(stream.TwitchStreamId);
                return;
            }

            switch (reason)
            {
                case SessionEndReason.StreamStopped:
                    await MarkStoppedAsync(stream);
                    return;

                case SessionEndReason.StreamError(var ex):
                    await HandleErrorAsync(stream, channel, ex);
                    return;

                case SessionEndReason.StreamEnded:
                default:
                    await HandleStreamEndedAsync(stream, channel);
                    break;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during session finalization.");
        }
    }

    private async Task MarkStoppedAsync(Domain.Stream stream)
    {
        stream.MarkAsStopped(dateTimeProvider.DateTimeNow);
        await streamRepository.UpdateAsync(stream);
        logger.LogDebug("Recording manually stopped.");
    }

    private async Task HandleErrorAsync(Domain.Stream stream, Channel channel, Exception ex)
    {
        logger.LogError(ex, "Session ended due to an error.");

        if (!await IsChannelLiveAsync(stream, channel))
            return;

        stream.MarkAsInterrupted();
        await streamRepository.UpdateAsync(stream);
        logger.LogWarning("Stream disconnected but still live on Twitch. Marked as interrupted.");
    }

    private async Task HandleStreamEndedAsync(Domain.Stream stream, Channel channel)
    {
        stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
        await streamRepository.UpdateAsync(stream);
        await channelRepository.SetLiveAsync(channel.Id, false);

        var duration = (stream.FinishedAt - stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        logger.LogInformation("Stream finished. Total duration: {Duration}.", duration);
    }

    private async Task<bool> IsChannelLiveAsync(Domain.Stream stream, Channel channel)
    {
        var metadata = await twitchClient.GetStreamMetadataAsync(channel.Name, CancellationToken.None);
        return metadata?.TwitchStreamId == stream.TwitchStreamId;
    }
}