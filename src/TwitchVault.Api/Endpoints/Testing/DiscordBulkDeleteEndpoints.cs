using System.Net;
using System.Text.Json;
using PolyStore;

namespace TwitchVault.Api.Endpoints.Testing;

public sealed record DiscordBulkDeleteRequest(
    string StartMessageId = "1544469567571755039",
    string? ChannelId = "1534946063272771788",
    string? Authorization = null,
    string? StorageInstance = "discord-main",
    int? MaxMessages = null);

public class DiscordBulkDeleteEndpoints : IEndpoint
{
    private const string DefaultChannelId = "1534946063272771788";

    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/discord").WithTags("Testing");

        group.MapPost("/bulk-delete", async (
            DiscordBulkDeleteRequest request,
            IPolyStore polyStore,
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordBulkDeleteEndpoints> logger,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.StartMessageId))
                return Results.BadRequest(new { error = "startMessageId is required." });

            var channelId = string.IsNullOrWhiteSpace(request.ChannelId)
                ? DefaultChannelId
                : request.ChannelId.Trim();

            var instanceName = string.IsNullOrWhiteSpace(request.StorageInstance)
                ? "discord-main"
                : request.StorageInstance.Trim();

            var authHeader = request.Authorization;
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                return Results.BadRequest(new
                {
                    error = "Discord authorization header or token is required. Provide 'authorization' in request body."
                });
            }

            var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authHeader);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "TwitchVault (DiscordCleanUp, 1.0)");

            var totalFetched = 0;
            var totalDeleted = 0;
            var batchesProcessed = 0;
            var currentAfterId = request.StartMessageId.Trim();
            var maxLimit = request.MaxMessages ?? int.MaxValue;

            logger.LogInformation("Starting Discord bulk delete on channel {ChannelId} after message {StartMessageId}",
                channelId, currentAfterId);

            while (!cancellationToken.IsCancellationRequested && totalDeleted < maxLimit)
            {
                var remainingToDelete = maxLimit - totalDeleted;
                var fetchLimit = Math.Clamp(remainingToDelete, 1, 100);
                var fetchUrl = $"https://discord.com/api/v9/channels/{channelId}/messages?after={currentAfterId}&limit={fetchLimit}";

                HttpResponseMessage? fetchResponse = null;
                for (var retry = 0; retry < 5; retry++)
                {
                    fetchResponse = await client.GetAsync(fetchUrl, cancellationToken);
                    if (fetchResponse.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        var retryAfter = await GetRetryAfterDelayAsync(fetchResponse);
                        logger.LogWarning("Discord rate limit hit while fetching messages. Waiting {Seconds}s...", retryAfter.TotalSeconds);
                        await Task.Delay(retryAfter, cancellationToken);
                        continue;
                    }
                    break;
                }

                if (fetchResponse is null || !fetchResponse.IsSuccessStatusCode)
                {
                    var statusCode = fetchResponse?.StatusCode;
                    var errorBody = fetchResponse != null ? await fetchResponse.Content.ReadAsStringAsync(cancellationToken) : "null";
                    logger.LogError("Failed to fetch Discord messages. Status: {StatusCode}, Body: {Body}", statusCode, errorBody);
                    return Results.Json(new
                    {
                        success = false,
                        error = $"Failed to fetch messages: {statusCode}",
                        details = errorBody,
                        totalFetched,
                        totalDeleted,
                        batchesProcessed
                    }, statusCode: (int)(statusCode ?? HttpStatusCode.InternalServerError));
                }

                using var fetchDoc = await fetchResponse.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken: cancellationToken);
                if (fetchDoc is null || fetchDoc.RootElement.ValueKind != JsonValueKind.Array)
                    break;

                var messageElements = fetchDoc.RootElement.EnumerateArray().ToList();
                if (messageElements.Count == 0)
                {
                    logger.LogInformation("No more messages found after {CurrentAfterId}. Completed.", currentAfterId);
                    break;
                }

                // Strictly cap at max 100 messages (and within remaining quota)
                var messageIds = messageElements
                    .Select(m => m.TryGetProperty("id", out var idProp) ? idProp.GetString() : null)
                    .Where(id => !string.IsNullOrEmpty(id))
                    .Select(id => id!)
                    .Take(fetchLimit)
                    .Take(100)
                    .ToList();

                if (messageIds.Count == 0)
                    break;

                totalFetched += messageIds.Count;
                var highestId = messageIds.Select(ulong.Parse).Max().ToString();

                // Format fake URLs for CloudStorageService: path is /{channelId}/{messageId}
                var fakeUrls = messageIds.Select(id => $"https://fake.com/{id}/{channelId}").ToList();

                var deleteResult = await polyStore.DeleteBatchAsync(
                    instanceName,
                    fakeUrls,
                    cancellationToken);

                if (deleteResult.IsSuccess)
                {
                    totalDeleted += messageIds.Count;
                    batchesProcessed++;
                    logger.LogInformation("Deleted batch #{Batch} of {Count} messages (Total deleted: {Total})",
                        batchesProcessed, messageIds.Count, totalDeleted);
                }
                else
                {
                    logger.LogWarning("Cloud storage delete failed for batch after {CurrentAfterId}: {Error}. Advancing past highest ID {HighestId} to avoid infinite loop.",
                        currentAfterId, deleteResult.Error, highestId);
                }

                currentAfterId = highestId;

                if (messageElements.Count < fetchLimit || totalDeleted >= maxLimit)
                {
                    break;
                }

                // 2 seconds delay between each bulk deletion
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            return Results.Ok(new
            {
                success = true,
                channelId,
                startMessageId = request.StartMessageId,
                totalFetched,
                totalDeleted,
                batchesProcessed
            });
        })
        .WithName("DiscordBulkDeleteAfter")
        .WithSummary("[Testing] Bulk delete Discord messages in channel after a given message ID using ICloudStorageService.")
        .Accepts<DiscordBulkDeleteRequest>("application/json");
    }

    private static async Task<TimeSpan> GetRetryAfterDelayAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
            if (doc != null && doc.RootElement.TryGetProperty("retry_after", out var retryAfterProp))
            {
                if (retryAfterProp.TryGetDouble(out var seconds))
                    return TimeSpan.FromSeconds(Math.Max(seconds, 0.5));
            }
        }
        catch { }

        return TimeSpan.FromSeconds(2);
    }
}