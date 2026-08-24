using TwitchVault.Api.Common;
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
    IStreamStorageService storageService,
    ITwitchGqlClient twitchClient,
    IDateTimeProvider dateTimeProvider,
    IRecordingOrchestrator recording,
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
            await channelRepository.SetLiveAsync(stream.ChannelId, false);

            switch (reason)
            {
                case SessionEndReason.StreamStopped:
                    await MarkStoppedAsync(stream, sizeBytes);
                    break;

                case SessionEndReason.StreamError(var ex):
                    await HandleErrorAsync(stream, channelName, ex, sizeBytes);
                    break;

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

    private async Task MarkStoppedAsync(Domain.Stream stream, long sizeBytes)
    {
        stream.MarkAsStopped(dateTimeProvider.DateTimeNow);
        stream.SetSize(sizeBytes);
        await streamRepository.UpdateAsync(stream);
        await storageService.TryFinalizeStorageAsync(stream);
        logger.LogDebug("Recording manually stopped.");
    }

    private async Task HandleErrorAsync(Domain.Stream stream, string channelName, Exception ex, long sizeBytes)
    {
        logger.LogError(ex, "Session ended due to an error.");

        if (!await IsChannelLiveAsync(stream, channelName))
        {
            await HandleStreamEndedAsync(stream, sizeBytes);
            return;
        }

        // Channel is still live with the same stream — transient disconnect, mark interrupted and wait for reconnect.
        stream.MarkAsInterrupted();
        await streamRepository.UpdateAsync(stream);
        logger.LogWarning("Stream disconnected but still live on Twitch. Marked as interrupted.");
        await recording.HandleStreamOnlineAsync(stream.ChannelId, channelName);
    }

    private async Task HandleStreamEndedAsync(Domain.Stream stream, long sizeBytes)
    {
        stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
        stream.SetSize(sizeBytes);
        await streamRepository.UpdateAsync(stream);
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