using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using System.Net;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.Backblaze;

public sealed class BackblazeStorageService(
    IAmazonS3 client,
    IOptions<BackblazeStorageOptions> options)
{
    private readonly BackblazeStorageOptions _options = options.Value;

    public BackblazeStorageOptions Options => _options;

    public async Task<Result> UploadFileAsync(
        string localPath,
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey.Replace('\\', '/'),
                FilePath = localPath,
                ContentType = ResolveContentType(localPath)
            }, cancellationToken);

            if (response.HttpStatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
                return Result.Success;

            return Error.Failure($"Backblaze upload returned non-success status code {(int)response.HttpStatusCode}.");
        }
        catch (AmazonS3Exception ex)
        {
            if (ex.StatusCode == HttpStatusCode.TooManyRequests ||
                string.Equals(ex.ErrorCode, "TooManyRequests", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ex.ErrorCode, "SlowDown", StringComparison.OrdinalIgnoreCase))
            {
                return Error.TooManyRequests($"Backblaze rate limit hit: {ex.Message}");
            }

            if (ex.StatusCode == HttpStatusCode.Forbidden ||
                ex.StatusCode == (HttpStatusCode)507 || // Insufficient Storage
                string.Equals(ex.ErrorCode, "QuotaExceeded", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ex.ErrorCode, "InsufficientStorage", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ex.ErrorCode, "StorageQuotaExceeded", StringComparison.OrdinalIgnoreCase))
            {
                return Error.Forbidden($"Backblaze quota exceeded: {ex.Message}");
            }

            return Error.Failure($"S3 error uploading file '{objectKey}': {ex.ErrorCode} - {ex.Message}");
        }
        catch (Exception ex)
        {
            return Error.Failure($"Error uploading file '{objectKey}': {ex.Message}");
        }
    }

    public string GetPreSignedUrl(string objectKey, TimeSpan lifetime) =>
        client.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = objectKey.Replace('\\', '/'),
            Verb = HttpVerb.GET,
            Expires = DateTime.UtcNow.Add(lifetime)
        });

    public async Task<Result> DeleteObjectAsync(
        string objectKey,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _options.BucketName,
                Key = objectKey.Replace('\\', '/')
            }, cancellationToken);
            return Result.Success;
        }
        catch (Exception ex)
        {
            return Error.Failure($"Error deleting object '{objectKey}': {ex.Message}");
        }
    }

    private static string ResolveContentType(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".ts" => "video/mp2t",
            ".mp4" or ".m4s" => "video/mp4",
            ".m3u8" => "application/vnd.apple.mpegurl",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
}