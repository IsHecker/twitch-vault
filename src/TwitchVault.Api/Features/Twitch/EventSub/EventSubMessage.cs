using System.Text.Json.Serialization;

namespace TwitchVault.Api.Features.Twitch.EventSub;

public record struct EventSubSubscriptionResponse(
    [property: JsonPropertyName("data")] EventSubSubscription[] Data,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("total_cost")] int TotalCost,
    [property: JsonPropertyName("max_total_cost")] int MaxTotalCost,
    [property: JsonPropertyName("pagination")] PaginationInfo? Pagination);

public record struct EventSubSubscription(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("condition")] Condition Condition,
    [property: JsonPropertyName("transport")] Transport Transport,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public record struct Condition(
    [property: JsonPropertyName("broadcaster_user_id")] string BroadcasterUserId,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("moderator_user_id")] string? ModeratorUserId);

public record struct Transport(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("callback")] string? Callback);

public record struct PaginationInfo([property: JsonPropertyName("cursor")] string? Cursor);