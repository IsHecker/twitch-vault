using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchVault.Api.Twitch.EventSub;

public record struct EventSubMessage(
    [property: JsonPropertyName("metadata")] Metadata Metadata,
    [property: JsonPropertyName("payload")] Payload Payload);

public record struct EventSubSubscriptionResponse(
    [property: JsonPropertyName("data")] Subscription[] Data,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("total_cost")] int TotalCost,
    [property: JsonPropertyName("max_total_cost")] int MaxTotalCost);

public record struct Metadata(
    [property: JsonPropertyName("message_id")] string MessageId,
    [property: JsonPropertyName("message_type")] string MessageType,
    [property: JsonPropertyName("message_timestamp")] DateTime MessageTimestamp);

public record struct Payload(
    [property: JsonPropertyName("session")] Session Session,
    [property: JsonPropertyName("subscription")] Subscription Subscription,
    [property: JsonPropertyName("event")] JsonDocument Event);

public record struct Session(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("keepalive_timeout_seconds")] int? KeepaliveTimeoutSeconds,
    [property: JsonPropertyName("reconnect_url")] string? ReconnectUrl);

public record struct Subscription(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("condition")] Condition Condition,
    [property: JsonPropertyName("transport")] Transport Transport,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt);

public record struct Condition(
    [property: JsonPropertyName("broadcaster_user_id")] string? BroadcasterUserId,
    [property: JsonPropertyName("user_id")] string? UserId,
    [property: JsonPropertyName("moderator_user_id")] string? ModeratorUserId);

public record struct Transport(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("session_id")] string? SessionId,
    [property: JsonPropertyName("callback")] string? Callback);