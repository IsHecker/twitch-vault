using System.Net;
using System.Net.Http.Headers;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage.Catbox;

public sealed class CatboxApiClient(IHttpClientFactory httpClientFactory)
{
    private const string BaseUrl = "https://catbox.moe/user/api.php";
    private const int MaxRetryAttempts = 3;
    private const int DefaultMaxConcurrency = 3;
    private const int DefaultTimeoutSeconds = 100;

    private SemaphoreSlim? _gate;
    private int _gateCapacity;

    public async Task<Result<string>> UploadFileAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        string? userHash,
        StorageBehaviorOptions behavior,
        CancellationToken cancellationToken)
    {
        var gate = GetGate(behavior);
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetTimeout(behavior));

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

            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            content.Add(streamContent, "fileToUpload", fileName);
            request.Content = content;

            using var client = httpClientFactory.CreateClient();
            var bodyResult = await SendAsync(
                () => client.SendAsync(request, timeoutCts.Token), timeoutCts.Token);

            if (bodyResult.IsFailure)
                return Error.Failure($"Update failed: {bodyResult.Error}");

            if (!Uri.TryCreate(bodyResult.Value, UriKind.Absolute, out _))
                return Error.Failure("InvalidResponse", $"Catbox returned unexpected response: {bodyResult.Value}");

            return bodyResult.Value;
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("NetworkError", ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Result<string>> DeleteFilesAsync(
        IEnumerable<string> fileNames,
        string userHash,
        StorageBehaviorOptions behavior,
        CancellationToken cancellationToken)
    {
        var gate = GetGate(behavior);
        await gate.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(GetTimeout(behavior));

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

            using var client = httpClientFactory.CreateClient();
            var bodyResult = await SendAsync(() => client.SendAsync(request, timeoutCts.Token), timeoutCts.Token);
            if (bodyResult.IsFailure)
                return Error.Failure($"Delete failed: {bodyResult.Error}");

            if (bodyResult.Value.Contains("error", StringComparison.OrdinalIgnoreCase))
                return Error.Failure("CatboxError", bodyResult.Value);

            return bodyResult.Value;
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("NetworkError", ex.Message);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<Result<string>> SendAsync(
        Func<Task<HttpResponseMessage>> action,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxRetryAttempts; attempt++)
        {
            using var response = await action();
            var isTransientError = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500;

            if (isTransientError && attempt < MaxRetryAttempts)
            {
                var delay = response.Headers.RetryAfter?.Delta
                    ?? TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 250));

                await Task.Delay(delay, cancellationToken);
                continue;
            }

            var body = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (response.IsSuccessStatusCode)
                return body;

            return Error.Failure("CatboxApiError", $"{(int)response.StatusCode} — {body}");
        }

        throw new InvalidOperationException("Unreachable.");
    }

    private SemaphoreSlim GetGate(StorageBehaviorOptions behavior)
    {
        var capacity = behavior.MaxConcurrentUploads > 0 ? behavior.MaxConcurrentUploads : DefaultMaxConcurrency;
        if (_gate is null || _gateCapacity != capacity)
        {
            _gate = new SemaphoreSlim(capacity, capacity);
            _gateCapacity = capacity;
        }

        return _gate;
    }

    private static TimeSpan GetTimeout(StorageBehaviorOptions behavior) =>
        behavior.RequestTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(behavior.RequestTimeoutSeconds)
            : TimeSpan.FromSeconds(DefaultTimeoutSeconds);
}