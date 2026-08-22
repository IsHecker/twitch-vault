using System.Net;
using System.Net.Http.Headers;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage.Catbox;

public sealed class CatboxApiClient(IHttpClientFactory httpClientFactory)
{
    private const string BaseUrl = "https://catbox.moe/user/api.php";

    public async Task<Result<string>> UploadFileAsync(
        StorageFile storageFile,
        string? userHash,
        StorageInstanceOptions instanceOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(instanceOptions.Behavior.RequestTimeoutSeconds));

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");
            using var content = new MultipartFormDataContent
            {
                { new StringContent("fileupload"), "reqtype" }
            };

            if (!string.IsNullOrWhiteSpace(userHash))
                content.Add(new StringContent(userHash), "userhash");

            using var streamContent = new StreamContent(storageFile.Content);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(storageFile.ContentType);
            content.Add(streamContent, "fileToUpload", storageFile.FileName);
            request.Content = content;

            using var client = httpClientFactory.CreateClient(instanceOptions.Name);
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = (await response.Content.ReadAsStringAsync(timeoutCts.Token)).Trim();

            if (!response.IsSuccessStatusCode)
                return Error.Failure("CatboxApiError", $"{(int)response.StatusCode} — {body}");

            if (string.IsNullOrEmpty(body))
                return Error.Failure("EmptyResponse", "Catbox returned an empty response.");

            if (!Uri.TryCreate(body, UriKind.Absolute, out _))
                return Error.Failure("InvalidResponse", $"Catbox returned unexpected response: {body}");

            return body;
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("NetworkError", ex.Message);
        }
    }

    public async Task<Result<string>> DeleteFilesAsync(
        IEnumerable<string> fileNames,
        string userHash,
        StorageInstanceOptions instanceOptions,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(instanceOptions.Behavior.RequestTimeoutSeconds));

            var files = string.Join(" ", fileNames);

            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36");

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["reqtype"] = "deletefiles",
                ["userhash"] = userHash,
                ["files"] = files
            });
            request.Content = content;

            using var client = httpClientFactory.CreateClient(instanceOptions.Name);
            using var response = await client.SendAsync(request, timeoutCts.Token);
            var body = (await response.Content.ReadAsStringAsync(timeoutCts.Token)).Trim();

            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                return body;

            if (!response.IsSuccessStatusCode)
                return Error.Failure("CatboxApiError", $"{(int)response.StatusCode} — {body}");

            if (body.Contains("error", StringComparison.OrdinalIgnoreCase))
                return Error.Failure("CatboxError", body);

            return body;
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("NetworkError", ex.Message);
        }
    }
}