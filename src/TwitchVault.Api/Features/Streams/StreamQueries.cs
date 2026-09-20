using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Streams;

public static class StreamQueries
{
    public static Task<TwitchVault.Api.Features.Streams.Stream?> GetByIdAsync(this IQueryable<TwitchVault.Api.Features.Streams.Stream> query, string id) =>
        query.FirstOrDefaultAsync(s => s.Id == id);

    public static IQueryable<TwitchVault.Api.Features.Streams.Stream> ById(this IQueryable<TwitchVault.Api.Features.Streams.Stream> query, string id) =>
        query.Where(s => s.Id == id);

    public static IQueryable<TwitchVault.Api.Features.Streams.Stream> ForChannel(this IQueryable<TwitchVault.Api.Features.Streams.Stream> query, string channelId) =>
        query.Where(s => s.ChannelId == channelId);

    public static IQueryable<TwitchVault.Api.Features.Streams.Stream> PendingUpload(this IQueryable<TwitchVault.Api.Features.Streams.Stream> query) =>
        query.Where(s => s.Status == StreamStatus.Finished
            && (s.StorageLocation == StorageLocation.Local || s.StorageOperationStatus == StorageOperationStatus.UploadFailed));

    public static IQueryable<TwitchVault.Api.Features.Streams.Stream> PendingDeletion(this IQueryable<TwitchVault.Api.Features.Streams.Stream> query) =>
        query.Where(s => s.Status != StreamStatus.Recording
            && (s.StorageOperationStatus == StorageOperationStatus.DeleteRequest || s.StorageOperationStatus == StorageOperationStatus.Deleting));

    public static IQueryable<TwitchVault.Api.Features.Streams.Stream> StaleActive(this IQueryable<TwitchVault.Api.Features.Streams.Stream> query, string channelId, string? currentTwitchStreamId = null) =>
        query.Where(s => s.ChannelId == channelId
            && s.FinishedAt == null
            && (currentTwitchStreamId == null || s.Id != currentTwitchStreamId));
}