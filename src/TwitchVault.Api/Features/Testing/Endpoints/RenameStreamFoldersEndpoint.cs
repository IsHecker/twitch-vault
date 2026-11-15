using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Features.Testing.Endpoints;

public class RenameStreamFoldersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/streams").WithTags("Testing");

        group.MapPost("/rename-folders", async (
            AppDbContext dbContext,
            IWebHostEnvironment env,
            IOptions<PathsOptions> pathsOptions,
            ILogger<RenameStreamFoldersEndpoint> logger,
            bool dryRun,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Starting stream folder rename migration (DryRun: {DryRun})...", dryRun);

            var streams = await dbContext.Streams
                .Include(s => s.Channel)
                .ToListAsync(cancellationToken);

            var results = new List<StreamMigrationResult>();
            int renamedDiskCount = 0;
            int dbUpdatedCount = 0;

            foreach (var stream in streams)
            {
                var channelName = stream.Channel?.Name;
                if (string.IsNullOrWhiteSpace(channelName))
                {
                    var parts = stream.Folder.RelativePath.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries);
                    channelName = parts.Length > 1 ? parts[1] : "unknown";
                }

                var newFolder = StreamFolder.Create(pathsOptions.Value.Streams, channelName, stream.Id);
                var isFolderDifferent = !string.Equals(stream.Folder.RelativePath, newFolder.RelativePath, StringComparison.OrdinalIgnoreCase);

                var oldDiskPath = stream.Folder.GetAbsolutePath(env.ContentRootPath);
                var newDiskPath = newFolder.GetAbsolutePath(env.ContentRootPath);

                string diskStatus;

                if (Directory.Exists(oldDiskPath))
                {
                    if (string.Equals(Path.GetFullPath(oldDiskPath), Path.GetFullPath(newDiskPath), StringComparison.OrdinalIgnoreCase))
                    {
                        diskStatus = "Already migrated";
                    }
                    else if (Directory.Exists(newDiskPath))
                    {
                        diskStatus = "Conflict: Target directory already exists";
                        logger.LogWarning("Cannot rename '{OldPath}' -> '{NewPath}' because target already exists.", oldDiskPath, newDiskPath);
                    }
                    else
                    {
                        if (!dryRun)
                        {
                            var parentDir = Path.GetDirectoryName(newDiskPath);
                            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                            {
                                Directory.CreateDirectory(parentDir);
                            }

                            Directory.Move(oldDiskPath, newDiskPath);
                            diskStatus = "Renamed";
                            renamedDiskCount++;
                        }
                        else
                        {
                            diskStatus = "[DryRun] Would rename";
                            renamedDiskCount++;
                        }
                    }
                }
                else
                {
                    diskStatus = Directory.Exists(newDiskPath)
                        ? "Already migrated"
                        : "Not on disk (remote/cleaned)";
                }

                if (isFolderDifferent)
                {
                    if (!dryRun)
                    {
                        stream.UpdateFolder(newFolder);
                    }

                    dbUpdatedCount++;
                }

                results.Add(new StreamMigrationResult(
                    stream.Id,
                    channelName,
                    stream.Folder.RelativePath,
                    newFolder.RelativePath,
                    diskStatus,
                    isFolderDifferent ? (dryRun ? "[DryRun] Would update" : "Updated") : "Up to date"
                ));
            }

            if (!dryRun && dbUpdatedCount > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Successfully updated {Count} stream records in the database.", dbUpdatedCount);
            }

            // Also update Database.json if present
            int jsonUpdatedCount = 0;
            var databaseJsonPath = Path.Combine(env.ContentRootPath, "Database.json");
            if (File.Exists(databaseJsonPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(databaseJsonPath, cancellationToken);
                    var rootNode = JsonNode.Parse(content);
                    if (rootNode?["Streams"] is JsonArray streamsArray)
                    {
                        var newFolderLookup = results.ToDictionary(r => r.StreamId, r => r.NewFolder);

                        foreach (var streamNode in streamsArray)
                        {
                            if (streamNode is null)
                                continue;

                            var id = streamNode["Id"]?.GetValue<string>();
                            if (id != null && newFolderLookup.TryGetValue(id, out var targetFolder))
                            {
                                var current = streamNode["Folder"]?.GetValue<string>();
                                if (!string.Equals(current, targetFolder, StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!dryRun)
                                    {
                                        streamNode["Folder"] = targetFolder;
                                    }

                                    jsonUpdatedCount++;
                                }
                            }
                        }

                        if (!dryRun && jsonUpdatedCount > 0)
                        {
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            await File.WriteAllTextAsync(databaseJsonPath, rootNode.ToJsonString(options), cancellationToken);
                            logger.LogInformation("Successfully updated {Count} stream entries in Database.json.", jsonUpdatedCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to update Database.json during stream folder migration.");
                }
            }

            return Results.Ok(new
            {
                Success = true,
                DryRun = dryRun,
                TotalStreams = streams.Count,
                FoldersRenamedOnDisk = renamedDiskCount,
                DatabaseStreamsUpdated = dbUpdatedCount,
                DatabaseJsonEntriesUpdated = jsonUpdatedCount,
                Streams = results
            });
        })
        .WithName("RenameStreamFolders")
        .WithSummary("[DevOnly] Renames recorded stream directories on disk and updates database/Database.json records to use stream ID instead of timestamps.")
        .DisableAntiforgery();
    }

    public record StreamMigrationResult(
        string StreamId,
        string Channel,
        string OldFolder,
        string NewFolder,
        string DiskStatus,
        string DatabaseStatus
    );
}