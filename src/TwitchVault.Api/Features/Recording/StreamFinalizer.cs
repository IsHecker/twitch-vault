namespace TwitchVault.Api.Features.Recording;

public interface IStreamFinalizer
{
    Task FinalizeAsync(
        Channel channel,
        Streams.Stream stream,
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
    IDataStore dataStore,
    IStreamStorageService storageService,
    ITwitchGqlClient twitchClient,
    ITwitchHelixClient helixClient,
    ILogger<StreamFinalizer> logger) : IStreamFinalizer
{
    public async Task FinalizeAsync(
        Channel channel,
        Streams.Stream stream,
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

    private async Task MarkStoppedAsync(Streams.Stream stream, long sizeBytes)
    {
        await TryResolveVodAsync(stream);

        var totalDurationSec = ReadPlaylistTotalDuration(stream.Folder.PlaylistPath);
        var finishedAt = stream.StartedAt.AddSeconds(totalDurationSec);

        stream.MarkAsStopped(finishedAt);
        stream.SetSize(sizeBytes);

        await storageService.FinalizeStorageAsync(stream);
    }

    private async Task HandleErrorAsync(Streams.Stream stream, string channelName, Exception ex, long sizeBytes)
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

    private async Task HandleStreamEndedAsync(Streams.Stream stream, long sizeBytes)
    {
        await TryResolveVodAsync(stream);

        var totalDurationSec = ReadPlaylistTotalDuration(stream.Folder.PlaylistPath);
        var finishedAt = stream.StartedAt.AddSeconds(totalDurationSec);

        stream.MarkAsFinished(finishedAt);
        stream.SetSize(sizeBytes);

        await storageService.FinalizeStorageAsync(stream);
        var durationStr = (stream.FinishedAt - stream.StartedAt)?.ToString(@"hh\:mm\:ss") ?? "unknown";
        logger.LogInformation("Stream finished. Total duration: {Duration} ({Instance}).", durationStr, stream.StorageInstanceName);
    }

    private async Task TryResolveVodAsync(Streams.Stream stream)
    {
        if (stream.VodId is not null)
            return;

        var vod = await helixClient.GetRecentStreamVodAsync(stream.ChannelId!, stream.Id, CancellationToken.None);
        if (vod is null)
            return;

        stream.SetVodId(vod.Value.Id);
        stream.UpdateStartedAt(vod.Value.CreatedAt);
    }

    private static float ReadPlaylistTotalDuration(string playlistPath)
    {
        if (!File.Exists(playlistPath))
            return 0f;

        using var stream = new FileStream(playlistPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.StartsWith(HlsTags.TotalSecondsPrefix, StringComparison.Ordinal))
            {
                var value = HlsTagReader.ReadTagValue(line, HlsTags.TotalSecondsPrefix);
                if (float.TryParse(value, out var duration))
                    return duration;
            }

            if (line.StartsWith(HlsTags.ExtInfPrefix, StringComparison.Ordinal))
                break;
        }

        return 0f;
    }

    private async Task<bool> IsChannelLiveAsync(Streams.Stream stream, string channelName)
    {
        var metadata = await twitchClient.GetStreamMetadataAsync(channelName, CancellationToken.None);
        return metadata?.Id == stream.Id;
    }

    private Task UpdateStream(Streams.Stream stream)
    {
        return dataStore.ExecuteAsync(() =>
        {
            dataStore.Save(stream);
            return Task.CompletedTask;
        });
    }
}