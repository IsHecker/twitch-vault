using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage.Catbox;

[StorageProvider(CloudProviderType.Catbox)]
public sealed class CatboxCloudStorageProviderNew(
    CatboxApiClient catboxClient,
    LiveOptions<StorageInstanceOptions> options) : ICloudStorageProvider
{
    public StorageInstanceOptions Options => options.Value;

    public async Task<Result<IEnumerable<RemoteUrl>>> UploadAsync(
        IEnumerable<StorageFile> files,
        CancellationToken cancellationToken)
    {
        var fileList = files.ToList();
        if (fileList.Count == 0)
            return Enumerable.Empty<RemoteUrl>().ToResult();

        var remoteUrls = new List<RemoteUrl>();
        foreach (var file in fileList)
        {
            var result = await UploadSingleAsync(file, cancellationToken);
            if (result.IsFailure)
                return Error.Failure("UploadFailed", result.Error.Message);

            remoteUrls.Add(result.Value);
            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(1, 4)), cancellationToken);
        }

        return remoteUrls;
    }

    private async Task<Result<RemoteUrl>> UploadSingleAsync(StorageFile file, CancellationToken cancellationToken)
    {
        if (file.Content.Length > Options.Behavior.MaxFileSizeBytes)
            return Error.Failure("FileTooLarge", $"File '{file.FileName}' ({file.Content.Length} bytes) exceeds limit.");

        var urlResult = await catboxClient.UploadFileAsync(
            file,
            Options.Credentials.AccessToken,
            Options,
            cancellationToken);

        return urlResult.IsSuccess ? new RemoteUrl(file.FileName, urlResult.Value) : urlResult.Error;
    }

    public async Task<Result> DeleteAsync(
        IEnumerable<string> remoteUrls,
        CancellationToken cancellationToken)
    {
        _ = remoteUrls.TryGetNonEnumeratedCount(out var urlCount);
        if (!remoteUrls.Any())
            return Result.Success;

        if (string.IsNullOrWhiteSpace(Options.Credentials.AccessToken))
            return Error.Failure("DeleteFailed", "User hash required for deletions. Set AccessToken in credentials.");

        var fileNames = remoteUrls
            .Select(ExtractFileName)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToList();

        if (fileNames.Count == 0)
            return Error.Failure("DeleteFailed", "No valid filenames could be extracted from URLs.");

        var batchSize = Math.Max(1, Options.Behavior.MaxUploadBatchSize);
        var batches = fileNames.Chunk(batchSize);

        foreach (var batch in batches)
        {
            var result = await catboxClient.DeleteFilesAsync(batch!, Options.Credentials.AccessToken!, Options, cancellationToken);

            if (result.IsFailure)
                return Error.Failure("DeleteFailed", result.Error.Message);

            await Task.Delay(TimeSpan.FromSeconds(Random.Shared.Next(1, 4)), cancellationToken);
        }
        return Result.Success;
    }

    private static string? ExtractFileName(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Path.GetFileName(uri.LocalPath);

        return Path.GetFileName(url);
    }
}