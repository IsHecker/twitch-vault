using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Recording;

public interface IStreamFinalizer
{
    Task FinalizeAsync(
        string channelName,
        Domain.Stream stream,
        long sizeBytes,
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
    IStreamStorageService storageService,
    ITwitchGqlClient twitchClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<StreamFinalizer> logger) : IStreamFinalizer
{
    public async Task FinalizeAsync(
        string channelName,
        Domain.Stream stream,
        long sizeBytes,
        SessionEndReason reason)
    {
        try
        {
            if (stream.StorageOperationStatus == StorageOperationStatus.DeleteRequest)
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
                    await HandleErrorAsync(stream, channelName, ex);
                    return;

                case SessionEndReason.StreamEnded:
                default:
                    await HandleStreamEndedAsync(stream, sizeBytes);
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
        await storageService.TryFinalizeStorageAsync(stream);
        logger.LogDebug("Recording manually stopped.");
    }

    private async Task HandleErrorAsync(Domain.Stream stream, string channelName, Exception ex)
    {
        logger.LogError(ex, "Session ended due to an error.");

        if (!await IsChannelLiveAsync(stream, channelName))
            return;

        stream.MarkAsInterrupted();
        await streamRepository.UpdateAsync(stream);
        logger.LogWarning("Stream disconnected but still live on Twitch. Marked as interrupted.");
    }

    private async Task HandleStreamEndedAsync(Domain.Stream stream, long sizeBytes)
    {
        stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
        stream.SetSize(sizeBytes);
        await streamRepository.UpdateAsync(stream);
        await channelRepository.SetLiveAsync(stream.ChannelId, false);
        await storageService.TryFinalizeStorageAsync(stream);

        var duration = (stream.FinishedAt - stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        logger.LogInformation("Stream finished. Total duration: {Duration}.", duration);
    }

    private async Task<bool> IsChannelLiveAsync(Domain.Stream stream, string channelName)
    {
        var metadata = await twitchClient.GetStreamMetadataAsync(channelName, CancellationToken.None);
        return metadata?.TwitchStreamId == stream.TwitchStreamId;
    }
}