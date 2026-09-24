using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using TwitchVault.Api.Database;

namespace TwitchVault.Api.Features.Testing.Endpoints;

public class StreamFolderMigrationEndpoints : IDevOnlyEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/streams").WithTags("Testing");

        group.MapPost("/migrate-folder-paths", async (
            AppDbContext dbContext,
            ILogger<StreamFolderMigrationEndpoints> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation("Starting migration of stream folder paths to relative strings...");

            // 1. Migrate SQL Server database records
            var streams = await dbContext.Streams.ToListAsync(cancellationToken);
            int dbUpdatedCount = 0;

            foreach (var stream in streams)
            {
                // Force EF Core to mark Folder as modified so it writes the converted string
                dbContext.Entry(stream).Property(s => s.Folder).IsModified = true;
                dbUpdatedCount++;
            }

            if (dbUpdatedCount > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Successfully updated {Count} stream records in the database.", dbUpdatedCount);
            }

            // 2. Also migrate Database.json if present
            int jsonUpdatedCount = 0;
            var databaseJsonPath = Path.Combine(Directory.GetCurrentDirectory(), "Database.json");
            if (File.Exists(databaseJsonPath))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(databaseJsonPath, cancellationToken);
                    var rootNode = JsonNode.Parse(content);
                    if (rootNode?["Streams"] is JsonArray streamsArray)
                    {
                        foreach (var streamNode in streamsArray)
                        {
                            if (streamNode?["Folder"] is JsonObject folderObj)
                            {
                                var relPath = folderObj["RelativePath"]?.GetValue<string>();
                                if (!string.IsNullOrEmpty(relPath))
                                {
                                    streamNode["Folder"] = relPath.Replace('\\', '/');
                                    jsonUpdatedCount++;
                                }
                            }
                        }

                        if (jsonUpdatedCount > 0)
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
                DatabaseStreamsMigrated = dbUpdatedCount,
                DatabaseJsonEntriesMigrated = jsonUpdatedCount,
                Message = $"Migration completed: {dbUpdatedCount} DB record(s) and {jsonUpdatedCount} Database.json entry/entries updated to relative paths."
            });
        })
        .WithName("MigrateStreamFolderPaths")
        .WithSummary("[DevOnly] Migrates all stream folder paths from legacy JSON objects to plain relative path strings in the database and Database.json.")
        .DisableAntiforgery();
    }
}
