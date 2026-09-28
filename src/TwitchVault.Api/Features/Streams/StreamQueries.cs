using Microsoft.EntityFrameworkCore;

namespace TwitchVault.Api.Features.Streams;

public static class StreamQueries
{
    public static Task<Stream?> GetByIdAsync(this IQueryable<Stream> query, string id) =>
        query.FirstOrDefaultAsync(s => s.Id == id);

    public static IQueryable<Stream> ById(this IQueryable<Stream> query, string id) =>
        query.Where(s => s.Id == id);

    public static IQueryable<Stream> ForChannel(this IQueryable<Stream> query, string channelId) =>
        query.Where(s => s.ChannelId == channelId);

    public static IQueryable<Stream> PendingUpload(this IQueryable<Stream> query) =>
        query.Where(s => s.Status == StreamStatus.Finished
            && (s.StorageLocation == StorageLocation.Local || s.StorageOperationStatus == StorageOperationStatus.UploadFailed));

    public static IQueryable<Stream> PendingDeletion(this IQueryable<Stream> query) =>
        query.Where(s => s.Status != StreamStatus.Recording
            && (s.StorageOperationStatus == StorageOperationStatus.DeleteRequest || s.StorageOperationStatus == StorageOperationStatus.Deleting));

    public static IQueryable<Stream> StaleActive(this IQueryable<Stream> query, string channelId, string? currentTwitchStreamId = null) =>
        query.Where(s => s.ChannelId == channelId
            && s.FinishedAt == null
            && (currentTwitchStreamId == null || s.Id != currentTwitchStreamId));

    public static IQueryable<Stream> EligibleForVodPrune(this IQueryable<Stream> query, DateTime cutoff) =>
        query.Where(s =>
            s.Status == StreamStatus.Finished
            && s.FinishedAt != null
            && s.VodId != null
            && s.VodCheckAttemptedAt == null
            && s.FinishedAt <= cutoff);
}