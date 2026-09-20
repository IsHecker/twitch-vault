namespace TwitchVault.Api.Features.Recording;

public interface IStreamFinalizer
{
    Task FinalizeAsync(
        Channel channel,
        TwitchVault.Api.Features.Streams.Stream stream,
        long sizeBytes,
        SessionEndReason reason);
}

public abstract record SessionEndReason
{
    public sealed record StreamEnded : SessionEndReason;
    public sealed record StreamStopped : SessionEndReason;
    public sealed record StreamError(Exception Ex) : SessionEndReason;
    public sealed record ServerShutdown : SessionEndReason;
}

public sealed class StreamFinalizer(
    IDataStore dataStore,
    IStreamStorageService storageService,
    ITwitchGqlClient twitchClient,
    IDateTimeProvider dateTimeProvider,
    ILogger<StreamFinalizer> logger) : IStreamFinalizer
{
    public async Task FinalizeAsync(
        Channel channel,
        TwitchVault.Api.Features.Streams.Stream stream,
        long sizeBytes,
        SessionEndReason reason)
    {
        try
        {
            await dataStore.ExecuteAsync(() =>
            {
                dataStore.Save(channel);
                channel.SetLive(false);
                return Task.CompletedTask;
            });

            switch (reason)
            {
                case SessionEndReason.StreamStopped:
                    await MarkStoppedAsync(stream, sizeBytes);
                    break;

                case SessionEndReason.StreamError(var ex):
                    await HandleErrorAsync(stream, channel.Name, ex, sizeBytes);
                    break;

                case SessionEndReason.ServerShutdown:
                    await HandleServerShutdownAsync(channel, stream, sizeBytes);
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
        finally
        {
            await UpdateStream(stream);
        }
    }

    private async Task HandleServerShutdownAsync(Channel channel, TwitchVault.Api.Features.Streams.Stream stream, long sizeBytes)
    {
        var isStillLive = await IsChannelLiveAsync(stream, channel.Name);

        if (isStillLive)
        {
            stream.MarkAsInterrupted();
            logger.LogInformation(
                "Server shutdown: stream '{StreamId}' for '{Channel}' marked as interrupted (channel still live on Twitch).",
                stream.Id, channel.Name);
        }
        else
        {
            await HandleStreamEndedAsync(stream, sizeBytes);
        }
    }

    private async Task MarkStoppedAsync(TwitchVault.Api.Features.Streams.Stream stream, long sizeBytes)
    {
        stream.MarkAsStopped(dateTimeProvider.DateTimeNow);
        stream.SetSize(sizeBytes);

        await storageService.FinalizeStorageAsync(stream);
        logger.LogDebug("Recording manually stopped.");
    }

    private async Task HandleErrorAsync(TwitchVault.Api.Features.Streams.Stream stream, string channelName, Exception ex, long sizeBytes)
    {
        logger.LogError(ex, "Session ended due to an error.");

        if (!await IsChannelLiveAsync(stream, channelName))
        {
            await HandleStreamEndedAsync(stream, sizeBytes);
            return;
        }

        stream.MarkAsInterrupted();
        logger.LogWarning("Stream disconnected but still live on Twitch. Marked as interrupted.");
    }

    private async Task HandleStreamEndedAsync(TwitchVault.Api.Features.Streams.Stream stream, long sizeBytes)
    {
        stream.MarkAsFinished(dateTimeProvider.DateTimeNow);
        stream.SetSize(sizeBytes);

        await storageService.FinalizeStorageAsync(stream);
        var duration = (stream.FinishedAt - stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        logger.LogInformation("Stream finished. Total duration: {Duration} ({Instance}).", duration, stream.StorageInstanceName);
    }

    private async Task<bool> IsChannelLiveAsync(TwitchVault.Api.Features.Streams.Stream stream, string channelName)
    {
        var metadata = await twitchClient.GetStreamMetadataAsync(channelName, CancellationToken.None);
        return metadata?.Id == stream.Id;
    }

    private Task UpdateStream(TwitchVault.Api.Features.Streams.Stream stream)
    {
        return dataStore.ExecuteAsync(() =>
        {
            dataStore.Save(stream);
            return Task.CompletedTask;
        });
    }
}