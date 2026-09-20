using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Extensions;

public static class StreamQueries
{
    public static Task<Domain.Stream?> GetByIdAsync(this IQueryable<Domain.Stream> query, string id) =>
        query.FirstOrDefaultAsync(s => s.Id == id);

    public static IQueryable<Domain.Stream> ById(this IQueryable<Domain.Stream> query, string id) =>
        query.Where(s => s.Id == id);

    public static IQueryable<Domain.Stream> ForChannel(this IQueryable<Domain.Stream> query, string channelId) =>
        query.Where(s => s.ChannelId == channelId);

    public static IQueryable<Domain.Stream> PendingUpload(this IQueryable<Domain.Stream> query) =>
        query.Where(s => s.Status == StreamStatus.Finished
            && (s.StorageLocation == StorageLocation.Local || s.StorageOperationStatus == StorageOperationStatus.UploadFailed));

    public static IQueryable<Domain.Stream> PendingDeletion(this IQueryable<Domain.Stream> query) =>
        query.Where(s => s.Status != StreamStatus.Recording
            && (s.StorageOperationStatus == StorageOperationStatus.DeleteRequest || s.StorageOperationStatus == StorageOperationStatus.Deleting));

    public static IQueryable<Domain.Stream> StaleActive(this IQueryable<Domain.Stream> query, string channelId, string? currentTwitchStreamId = null) =>
        query.Where(s => s.ChannelId == channelId
            && s.FinishedAt == null
            && (currentTwitchStreamId == null || s.Id != currentTwitchStreamId));
}