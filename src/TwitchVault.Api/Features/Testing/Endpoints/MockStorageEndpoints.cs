using PolyStore;

namespace TwitchVault.Api.Features.Testing.Endpoints;

public class MockStorageEndpoints : IDevOnlyEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/storage").WithTags("Testing");

        group.MapPost("/upload-file", () =>
        {
            // Desktop enumeration was removed for security reasons.
            // Provide a specific file path via the /delete-file or /rewrite endpoints instead.
            return Results.BadRequest("Direct file upload from a fixed path is not supported.");
        }).DisableAntiforgery();

        group.MapPost("/delete-file", async (
            string storageInstance,
            string[] urls,
            IPolyStore polyStore) =>
        {
            var result = await polyStore.DeleteAsync(storageInstance, urls, CancellationToken.None);
            return Results.Ok(result);
        });

        group.MapPost("/rewrite", async (string playlistPaty, string outputPath, string urlFilePath) =>
        {
            await HlsPlaylistRewriter.RewriteSegmentsAsync(playlistPaty, outputPath, urlFilePath, CancellationToken.None);
            return Results.Ok();
        });
    }
}