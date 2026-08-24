using Microsoft.Extensions.Options;
using TwitchVault.Api.Common;
using TwitchVault.Api.Common.Results;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Recording;

public class ChannelService(
    IChannelRepository channelRepository,
    IStreamRepository streamRepository,
    ITwitchGqlClient twitchGqlClient,
    TwitchSubscriptionService twitchSubscription,
    IRecordingOrchestrator recordingOrchestrator,
    IOptions<PathsOptions> pathsOptions) : IChannelService
{
    public async Task<Result<Channel>> AddChannelAsync(
        string channelName,
        int qualityRank,
        bool shouldRecord,
        CancellationToken cancellationToken = default)
    {
        var existingChannel = await channelRepository.GetByNameAsync(channelName);
        if (existingChannel is not null)
            return Error.Conflict($"Channel '{channelName}' is already being monitored.");

        var channelId = await twitchGqlClient.GetChannelIdAsync(channelName, cancellationToken);
        if (string.IsNullOrWhiteSpace(channelId))
            return Error.NotFound($"Channel '{channelName}' was not found on Twitch.");

        var channel = Channel.Create(channelId, channelName, qualityRank, shouldRecord);

        if (shouldRecord)
            _ = twitchSubscription.AddChannelsAsync([channel], cancellationToken);

        await channelRepository.AddAsync(channel);
        return channel;
    }

    public async Task<Result> DeleteChannelAsync(string channelId, CancellationToken cancellationToken = default)
    {
        var channel = await channelRepository.GetByIdAsync(channelId);
        if (channel is null)
            return Result.Failure(Error.NotFound($"Channel '{channelId}' was not found."));

        var streams = await streamRepository.ListByChannelIdAsync(channelId);
        if (channel.IsLive)
        {
            var stream = streams.OrderByDescending(s => s.StartedAt).FirstOrDefault();
            if (stream is not null)
            {
                await recordingOrchestrator.ToggleStreamDeletionAsync(stream.TwitchStreamId, true);
                await recordingOrchestrator.StopRecordingAsync(stream.TwitchStreamId);
            }
        }

        await channelRepository.DeleteAsync(channelId);
        await IOUtils.DeleteDirectoryWithRetriesAsync(Path.Combine(pathsOptions.Value.Streams, channel.Name));
        _ = twitchSubscription.RemoveChannelAsync(channel, cancellationToken);

        return Result.Success;
    }

    public async Task<Result> SetRecordingStatusAsync(string channelId, bool shouldRecord, CancellationToken cancellationToken = default)
    {
        var channel = await channelRepository.GetByIdAsync(channelId);
        if (channel is null)
            return Result.Failure(Error.NotFound($"Channel '{channelId}' was not found."));

        if (channel.ShouldRecord == shouldRecord)
            return Result.Failure(Error.Validation($"Recording status is already set to {shouldRecord}."));

        channel.SetRecordingStatus(shouldRecord);
        await channelRepository.UpdateAsync(channel);

        if (shouldRecord)
            _ = twitchSubscription.AddChannelsAsync([channel], cancellationToken);
        else
            _ = twitchSubscription.RemoveChannelAsync(channel, cancellationToken);

        return Result.Success;
    }

    public async Task<Result<Channel>> UpdateChannelQualityAsync(string channelId, int qualityRank)
    {
        var channel = await channelRepository.GetByIdAsync(channelId);
        if (channel is null)
            return Result.Failure<Channel>(Error.NotFound($"Channel '{channelId}' was not found."));

        channel.UpdateQualityRank(qualityRank);
        await channelRepository.UpdateAsync(channel);
        return channel;
    }
}