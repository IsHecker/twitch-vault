// using System.Text.Json;
// using TwitchLib.EventSub.Webhooks.Core;
// using TwitchLib.EventSub.Webhooks.Core.Models;
// using TwitchVault.Api.Configuration;

// namespace TwitchVault.Api.Endpoints.EventSub;

// internal sealed class EventSubWebhookEndpoint : IEndpoint
// {
//     public void MapEndpoint(IEndpointRouteBuilder app) =>
//         app.MapPost("/webhooks/twitch", async (
//             HttpContext context,
//             IEventSubWebhooks eventSubWebhooks,
//             SettingsService settings,
//             ILogger<EventSubWebhookEndpoint> logger,
//             CancellationToken cancellationToken) =>
//         {
//             var headers = context.Request.Headers;
//             var metadata = new WebhookEventSubMetadata
//             {
//                 MessageId = headers["Twitch-Eventsub-Message-Id"].ToString(),
//                 MessageTimestamp = headers["Twitch-Eventsub-Message-Timestamp"].ToString(),
//                 MessageSignature = headers["Twitch-Eventsub-Message-Signature"].ToString(),
//                 MessageType = headers["Twitch-Eventsub-Message-Type"].ToString(),
//                 SubscriptionType = headers["Twitch-Eventsub-Subscription-Type"].ToString(),
//                 SubscriptionVersion = headers["Twitch-Eventsub-Subscription-Version"].ToString()
//             };

//             if (metadata.MessageType == "webhook_callback_verification")
//             {
//                 using var doc = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
//                 var challenge = doc.RootElement.GetProperty("challenge").GetString();
//                 logger.LogInformation("TwitchLib verified webhook callback challenge.");
//                 return Results.Content(challenge, "text/plain", statusCode: StatusCodes.Status200OK);
//             }

//             if (metadata.MessageType == "notification")
//             {
//                 await eventSubWebhooks.ProcessNotificationAsync(metadata, context.Request.Body);
//                 return Results.NoContent();
//             }

//             if (metadata.MessageType == "revocation")
//             {
//                 await eventSubWebhooks.ProcessRevocationAsync(metadata, context.Request.Body);
//                 return Results.Ok();
//             }

//             return Results.Ok();
//         })
//         .WithTags("EventSub")
//         .WithName(nameof(EventSubWebhookEndpoint))
//         .WithSummary("Receives Twitch EventSub webhook notifications.")
//         .DisableAntiforgery();
// }