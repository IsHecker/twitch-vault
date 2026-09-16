using PolyStore;
using TwitchVault.Api.Storage;

namespace TwitchVault.Api.Endpoints.Testing;

public class MockStorageEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/storage").WithTags("Testing");

        group.MapPost("/upload-file", async (
            IPolyStore polyStore) =>
        {
            var files = new DirectoryInfo("C:\\Users\\Mhamed\\Desktop")
                .EnumerateFiles();

            var storageFiles = files.Select(file =>
                new FilePayload(
                    file.FullName,
                    "text/plain",
                    File.OpenRead(file.FullName)));

            var result = await polyStore.UploadAsync(
                storageFiles,
                null,
                CancellationToken.None);

            return result.IsFailure
                ? Results.Ok(result.Error)
                : Results.Ok(result);
        }).DisableAntiforgery();

        group.MapPost("/delete-file", async (
            string storageInstance,
            string[] urls,
            IPolyStore polyStore) =>
        {
            var result = await polyStore.DeleteBatchAsync(storageInstance, urls, CancellationToken.None);
            return Results.Ok(result);
        });

        group.MapPost("/rewrite", async (string playlistPaty, string outputPath, string urlFilePath) =>
        {
            await HlsPlaylistRewriter.RewriteSegmentsAsync(playlistPaty, outputPath, urlFilePath, CancellationToken.None);
            return Results.Ok();
        });
    }
}