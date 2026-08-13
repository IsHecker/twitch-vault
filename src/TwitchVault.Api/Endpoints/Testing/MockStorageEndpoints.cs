// using TwitchVault.Api.CloudStorage.Discord;

// namespace TwitchVault.Api.Endpoints.Testing;

// public class MockStorageEndpoints : IEndpoint
// {
//     public void MapEndpoint(IEndpointRouteBuilder app)
//     {
//         var group = app.MapGroup("/testing/storage")
//             .WithTags("Testing");

//         group.MapPost("/upload-file", async (
//             string[] localPaths,
//             string objectKey,
//             DiscordClient client) =>
//         {
//             return Results.Ok(await client.UploadAsync(localPaths));
//         });
//     }
// }