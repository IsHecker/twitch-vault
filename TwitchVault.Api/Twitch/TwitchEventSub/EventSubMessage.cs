using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitchVault.Api.Twitch.TwitchEventSub;

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

/*
{
  "metadata": {
    "message_id": "96a84f",
    "message_type": "session_welcome",
    "message_timestamp": "2026-04-24T18:00:00.000Z"
  },
  "payload": {
    "session": {
      "id": "AgoQhsC5VE-tR_SCVlH-c_54sBIGY2VsbC1h",
      "status": "connected",
      "connected_at": "2026-04-24T18:00:00.000Z",
      "keepalive_timeout_seconds": 10,
      "reconnect_url": null
    }
  }
}


{
  "metadata": {
    "message_id": "befa17",
    "message_type": "notification",
    "message_timestamp": "2026-04-24T18:05:00.000Z"
  },
  "payload": {
    "subscription": {
      "id": "f1c2a387-161a-49f9-a165-0f21d7a4e1c4",
      "type": "stream.online",
      "version": "1",
      "status": "enabled",
      "condition": { "broadcaster_user_id": "1337" },
      "transport": { "method": "websocket", "session_id": "AgoQ..." },
      "created_at": "2026-04-24T18:00:05.000Z"
    },
    "event": {
      "id": "41375549",
      "broadcaster_user_id": "1337",
      "broadcaster_user_login": "twitchdev",
      "broadcaster_user_name": "TwitchDev",
      "type": "live", 
      "started_at": "2026-04-24T18:04:55.000Z"
    }
  }
}


{
  "metadata": {
    "message_id": "e411c5",
    "message_type": "notification",
    "message_timestamp": "2026-04-24T18:10:00.000Z"
  },
  "payload": {
    "subscription": {
      "id": "ede6d3-..." ,
      "type": "channel.update",
      "version": "2",
      "status": "enabled",
      "condition": { "broadcaster_user_id": "1337" },
      "transport": { "method": "websocket", "session_id": "AgoQ..." },
      "created_at": "2026-04-24T18:00:10.000Z"
    },
    "event": {
      "broadcaster_user_id": "1337",
      "broadcaster_user_login": "twitchdev",
      "broadcaster_user_name": "TwitchDev",
      "title": "New Stream Title Here!",
      "language": "en",
      "category_id": "509658",
      "category_name": "Just Chatting",
      "content_classification_labels": [],
      "is_mature": false
    }
  }
}
*/