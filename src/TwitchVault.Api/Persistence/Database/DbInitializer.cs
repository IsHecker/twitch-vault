using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Persistence.Database;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();
        var pathsOptions = scope.ServiceProvider.GetService<IOptions<PathsOptions>>();

        await using var context = await contextFactory.CreateDbContextAsync();

        try
        {
            await context.Database.EnsureCreatedAsync();
            logger.LogInformation("Database ensured created successfully.");

            // Check if database is newly created and empty, and migrate data from Database.json if available
            var hasChannels = await context.Channels.AnyAsync();
            var jsonPath = pathsOptions?.Value?.Database ?? "Database.json";

            if (!hasChannels && File.Exists(jsonPath))
            {
                logger.LogInformation("Migrating legacy data from {JsonPath} into EF Core database...", jsonPath);
                var json = await File.ReadAllTextAsync(jsonPath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new JsonStringEnumConverter() }
                };

                var legacyData = JsonSerializer.Deserialize<AppDatabase>(json, options);
                if (legacyData != null)
                {
                    if (legacyData.Users.Count > 0)
                        await context.Users.AddRangeAsync(legacyData.Users);

                    if (legacyData.Channels.Count > 0)
                        await context.Channels.AddRangeAsync(legacyData.Channels);

                    if (legacyData.Streams.Count > 0)
                        await context.Streams.AddRangeAsync(legacyData.Streams);

                    if (legacyData.UserChannels.Count > 0)
                        await context.UserChannels.AddRangeAsync(legacyData.UserChannels);

                    await context.SaveChangesAsync();
                    logger.LogInformation("Successfully migrated {ChannelsCount} channels, {StreamsCount} streams, and {UsersCount} users from {JsonPath}.",
                        legacyData.Channels.Count, legacyData.Streams.Count, legacyData.Users.Count, jsonPath);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while initializing the database.");
            throw;
        }
    }
}
