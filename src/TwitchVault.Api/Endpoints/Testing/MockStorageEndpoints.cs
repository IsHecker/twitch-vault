using System.Text;
using TwitchVault.Api.CloudStorage;

namespace TwitchVault.Api.Endpoints.Testing;

public class MockStorageEndpoints : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/testing/storage")
            .WithTags("Testing");

        group.MapPost("/upload-file", async (
            string[] localPaths,
            ICloudStorageService service,
            IHttpClientFactory httpClientFactory) =>
        {
            var files = localPaths.Select(path => new StorageFile(path, "text/plain", File.OpenRead(path)));
            var r1 = service.UploadAsync(files, "catbox-main", CancellationToken.None);
            var r2 = service.UploadAsync(files, "catbox-main", CancellationToken.None);
            var r3 = service.UploadAsync(files, "catbox-main", CancellationToken.None);
            var r4 = service.UploadAsync(files, "catbox-main", CancellationToken.None);

            var errors = (await Task.WhenAll([r1, r2, r3, r4])).Select(r => r.Error);
            StringBuilder result = new();
            foreach (var error in errors)
            {
                result.AppendLine(error.ToString());
            }
            return Results.Ok(result.ToString());
        });

        group.MapDelete("/delete-file", async (
            string[] localPaths,
            ICloudStorageService service,
            IHttpClientFactory httpClientFactory) =>
        {
            var files = localPaths.Select(path => new StorageFile(path, "text/plain", File.OpenRead(path)));
            var result = await service.DeleteBatchAsync("catbox-main", localPaths, CancellationToken.None);
            return Results.Ok(result);
        });
    }
}