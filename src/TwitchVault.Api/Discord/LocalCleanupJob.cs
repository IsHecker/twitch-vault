namespace TwitchVault.Api.Discord;

// [DisallowConcurrentExecution]
// public sealed class LocalCleanupJob(
//     BackblazeStorageService storage,
//     IStreamRepository streamRepository,
//     IWebHostEnvironment env,
//     ILogger<LocalCleanupJob> logger) : IJob
// {
//     public async Task Execute(IJobExecutionContext context)
//     {
//         var retentionDays = storage.Options.LocalRetentionDays;
//         var cutoff = DateTime.UtcNow.AddDays(-retentionDays);

//         logger.LogInformation(
//             "LocalCleanupJob: scanning for streams uploaded to B2 before {Cutoff:yyyy-MM-dd}.",
//             cutoff);

//         var streams = await streamRepository.GetAllAsync();
//         var deleted = 0;

//         foreach (var stream in streams)
//         {
//             if (!IsEligibleForCleanup(stream, cutoff))
//                 continue;

//             var localDirectory = Path.Combine(env.ContentRootPath, stream.Folder.RelativePath);

//             if (!Directory.Exists(localDirectory))
//                 continue;

//             try
//             {
//                 await IOUtils.DeleteDirectoryWithRetriesAsync(localDirectory);
//                 stream.SetStorageLocation(StorageLocation.Remote);
//                 await streamRepository.UpdateAsync(stream);
//                 deleted++;

//                 logger.LogInformation(
//                     "LocalCleanupJob: deleted local files for stream {Title}", stream.Chapters[0].Title);
//             }
//             catch (Exception ex)
//             {
//                 logger.LogError(ex,
//                     "LocalCleanupJob: failed to delete local files for stream {StreamId}.",
//                     stream.TwitchStreamId);
//             }
//         }

//         logger.LogInformation("LocalCleanupJob: finished. {Count} stream(s) cleaned up.", deleted);
//     }

//     private static bool IsEligibleForCleanup(Domain.Stream stream, DateTime cutoff) =>
//         stream.Storage == StorageLocation.Remote &&
//         stream.FinishedAt.HasValue &&
//         stream.FinishedAt.Value.ToUniversalTime() < cutoff;
// }