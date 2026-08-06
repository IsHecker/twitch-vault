using TwitchVault.Api.Backblaze;

namespace TwitchVault.Api.Endpoints.Testing;

public class MockStorageEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/storage")
            .WithTags("Testing");

        group.MapPost("/upload-file", async (
            string localPath,
            string objectKey,
            BackblazeStorageService storage) =>
        {
            await storage.UploadFileAsync(localPath, objectKey);

            return Results.Ok();
        });

        group.MapPost("/upload-test", async (
            BackblazeStorageService storage) =>
        {
            var tempFile = Path.GetTempFileName();

            await File.WriteAllTextAsync(tempFile, "Hello Backblaze!");

            await storage.UploadFileAsync(
                tempFile,
                $"testing/{Guid.NewGuid()}.txt");

            File.Delete(tempFile);

            return Results.Ok();
        });

        group.MapGet("/presigned-url", (
            string objectKey,
            BackblazeStorageService storage) =>
        {
            var url = storage.GetPreSignedUrl(
                objectKey,
                TimeSpan.FromMinutes(30));

            return Results.Ok(url);
        });
    }
}