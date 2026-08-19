using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage;

public interface ICloudStorageService
{
    Task<Result<StorageUploadResponse>> UploadAsync(
        IEnumerable<StorageFile> files,
        string? preferredInstanceName = null,
        CancellationToken cancellationToken = default);

    Task<Result> DeleteBatchAsync(
        string instanceName,
        IEnumerable<string> remoteUrls,
        CancellationToken cancellationToken);
}

public sealed class CloudStorageService(
    StorageRouter router,
    StorageProviderRegistry providerRegistry,
    ILogger<CloudStorageService> logger) : ICloudStorageService
{
    public async Task<Result<StorageUploadResponse>> UploadAsync(
        IEnumerable<StorageFile> files,
        string? preferredInstanceName = null,
        CancellationToken cancellationToken = default)
    {
        var totalSizeBytes = files.Sum(f => f.Content.Length);

        var sessionResult = string.IsNullOrWhiteSpace(preferredInstanceName)
            ? await router.AcquireSessionAsync(totalSizeBytes, cancellationToken)
            : await router.AcquireSessionAsync(preferredInstanceName, totalSizeBytes, cancellationToken);

        if (sessionResult.IsFailure)
        {
            logger.LogError("Failed to acquire storage session for '{Instance}': {Error}",
                preferredInstanceName ?? "<auto>", sessionResult.Error);
            return sessionResult.Error;
        }

        using var session = sessionResult.Value;
        var provider = session.Provider;

        var uploadResult = await provider.UploadAsync(files, cancellationToken);
        if (uploadResult.IsFailure)
        {
            logger.LogError("Failed to upload this batch to provider '{Instance}': {Error}",
                provider.Options.Name, uploadResult.Error);
            return uploadResult.Error;
        }

        return new StorageUploadResponse(provider.Options.Name, uploadResult.Value);
    }

    public async Task<Result> DeleteBatchAsync(
        string instanceName,
        IEnumerable<string> remoteUrls,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instanceName))
            return Result.Success;

        try
        {
            var instance = providerRegistry.GetInstance(instanceName);
            return await instance.Provider.DeleteAsync(remoteUrls, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error deleting from provider '{Instance}'.", instanceName);
            return Error.Failure($"Failed to delete data: {ex.Message}");
        }
    }
}