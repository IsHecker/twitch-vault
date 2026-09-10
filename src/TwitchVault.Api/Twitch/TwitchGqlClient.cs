using System.Net;
using System.Text.Json;
using TwitchVault.Api.Common;

namespace TwitchVault.Api.Twitch;

public static class TwitchHttpClients
{
    public const string Api = "TwitchApi";
    public const string Cdn = "TwitchCdn";
}

public sealed class TwitchGqlClient(
    IHttpClientFactory httpClientFactory,
    ILogger<TwitchGqlClient> logger) : ITwitchGqlClient
{
    private const string TwitchGqlUrl = "https://gql.twitch.tv/gql";

    public async Task<Dictionary<Domain.Channel, bool>> IsChannelLiveAsync(List<Domain.Channel> channels, CancellationToken cancellationToken)
    {
        if (channels.Count == 0)
            return [];

        var payload = channels.Select(c => TwitchGqlPayloads.GetLiveStatus(c.Name));
        using var response = await SendGqlRequestAsync(payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var results = new Dictionary<Domain.Channel, bool>(channels.Count);

        var i = 0;
        foreach (var item in document!.RootElement.EnumerateArray())
        {
            var data = item.GetProperty("data");
            var user = data.GetProperty("user");
            results[channels[i]] = user.TryGetProperty("stream", out var stream) && stream.ValueKind != JsonValueKind.Null;
            i++;
        }

        return results;
    }

    public async Task<StreamMetadata?> GetStreamMetadataAsync(string channel, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var payload = TwitchGqlPayloads.StreamMetadata(channel);
            using var response = await SendGqlRequestAsync(payload, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
                if (document is not null)
                    return ParseStreamMetadata(document.RootElement);
            }

            if (attempt < 3 && !cancellationToken.IsCancellationRequested)
                await Task.Delay(TimeSpan.FromMilliseconds(400 * attempt), cancellationToken);
        }

        return null;
    }

    public async Task<Dictionary<Domain.Channel, StreamMetadata?>> GetStreamMetadataAsync(
        List<Domain.Channel> channels,
        CancellationToken cancellationToken)
    {
        var payloads = channels.Select(c => TwitchGqlPayloads.StreamMetadata(c.Name));
        using var response = await SendGqlRequestAsync(payloads, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return [];

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var results = new Dictionary<Domain.Channel, StreamMetadata?>(channels.Count);

        for (var i = 0; i < channels.Count; i++)
        {
            results[channels[i]] = ParseStreamMetadata(document!.RootElement[i]);
        }

        return results;
    }

    public async Task<string> GetMasterPlaylistAsync(string channel, CancellationToken cancellationToken)
    {
        try
        {
            var payload = TwitchGqlPayloads.PlaybackToken(channel);
            using var response = await SendGqlRequestAsync(payload, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return string.Empty;

            using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            if (document is null)
                return string.Empty;

            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return string.Empty;

            if (!data.TryGetProperty("streamPlaybackAccessToken", out var tokenElement) || tokenElement.ValueKind == JsonValueKind.Null)
                return string.Empty;

            var token = tokenElement.Deserialize<PlaybackToken>();
            var masterPlaylistUrl = BuildMasterPlaylistUrl(channel, token);

            using var apiClient = httpClientFactory.CreateClient(TwitchHttpClients.Api);
            var playlistResponse = await apiClient.GetAsync(masterPlaylistUrl, cancellationToken);
            if (!playlistResponse.IsSuccessStatusCode)
                return string.Empty;

            return await playlistResponse.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error fetching master playlist for channel '{Channel}'.", channel);
            return string.Empty;
        }
    }

    public async Task<string> GetPlaylistContentAsync(string playlistUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var apiClient = httpClientFactory.CreateClient(TwitchHttpClients.Api);
            var response = await apiClient.GetAsync(playlistUrl, cancellationToken);
            return response.IsSuccessStatusCode ?
                await response.Content.ReadAsStringAsync(cancellationToken)
                : string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Error fetching playlist content");
            return string.Empty;
        }
    }

    public async Task<Stream> DownloadAsStreamAsync(string url, CancellationToken cancellationToken)
    {
        HttpClient? cdnClient = null;
        HttpResponseMessage? response = null;
        try
        {
            cdnClient = httpClientFactory.CreateClient(TwitchHttpClients.Cdn);
            response = await cdnClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            // Transfer ownership: the stream disposes the response (and releases the connection)
            // when the caller disposes the stream returned by ReadAsStreamAsync.
            // We must NOT dispose response here — the network stream is still being read.
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            cdnClient.Dispose();
            return stream;
        }
        catch (Exception ex)
        {
            response?.Dispose();
            cdnClient?.Dispose();
            if (ex is not OperationCanceledException)
                logger.LogWarning(ex, "Transient network error downloading segment.");
            return Stream.Null;
        }
    }

    public async Task<string?> GetStreamVODIdAsync(string channel, CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.GetStreamVOD(channel);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var stream = document!.RootElement.GetProperty("data").GetProperty("user").GetProperty("stream");
        if (stream.ValueKind == JsonValueKind.Null)
            return null;

        var archiveVideo = stream.GetProperty("archiveVideo");
        var vodId = archiveVideo.ValueKind != JsonValueKind.Null ? archiveVideo.GetProperty("id").GetString() : null;
        return vodId;
    }

    public async Task<string?> GetChannelIdAsync(string channel, CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.GetChannelId(channel);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
        var user = document!.RootElement.GetProperty("data").GetProperty("user");

        return user.ValueKind == JsonValueKind.Null ? null : user.GetProperty("id").GetString();
    }

    private async Task<HttpResponseMessage> SendGqlRequestAsync(object payload, CancellationToken cancellationToken)
    {
        using var apiClient = httpClientFactory.CreateClient(TwitchHttpClients.Api);

        var request = new HttpRequestMessage(HttpMethod.Post, TwitchGqlUrl)
        {
            Content = JsonContent.Create(payload)
        };

        var response = await apiClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                logger.LogWarning("Twitch GQL rate limit (429) hit during API request.");
            else
                logger.LogWarning("Twitch GQL request failed with status code {StatusCode}.", response.StatusCode);
        }

        return response;
    }

    private static string BuildMasterPlaylistUrl(string channel, PlaybackToken token) =>
        $"https://usher.ttvnw.net/api/v2/channel/hls/{channel}.m3u8" +
        $"?acmb=eyJBcHBWZXJzaW9uIjoiYWE1NTk0ZDEtYjhkYy00NTMzLTgyNjItMTFhNWEwZTk5NTVmIiwiQ2xpZW50QXBwIjoidHdpbGlnaHQifQ%3D%3D" +
        $"&allow_source=true&browser_family=chrome&browser_version=147.0&cdm=wv&enable_score=true" +
        $"&fast_bread=true&include_unavailable=true&lang=en&os_name=Windows" +
        $"&os_version=NT%2010.0&p=8343545&platform=pwa&play_session_id=f4261c511c1b49e9b94c023814baebde" +
        $"&player_backend=mediaplayer&player_version=1.52.0-rc.3&playlist_include_framerate=true" +
        $"&reassignments_supported=true&sig={token.Signature}&supported_codecs=av1,h265,h264" +
        $"&token={token.Token}&transcode_mode=cbr_v1";

    private static StreamMetadata? ParseStreamMetadata(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
            return null;

        if (!data.TryGetProperty("user", out var user) || user.ValueKind == JsonValueKind.Null)
            return null;

        if (!user.TryGetProperty("stream", out var stream) || stream.ValueKind == JsonValueKind.Null)
            return null;

        var broadcastSettings = user.GetProperty("broadcastSettings");
        var streamId = stream.GetProperty("id").GetString()!;
        var startedAt = EgyptTimeProvider.ToEgyptDateTime(stream.GetProperty("createdAt").GetDateTimeOffset());

        var rawTitle = broadcastSettings.GetProperty("title").GetString();
        var title = string.IsNullOrWhiteSpace(rawTitle) ? "Untitled Stream" : rawTitle;

        var game = broadcastSettings.GetProperty("game");
        var gameId = game.ValueKind != JsonValueKind.Null
            ? game.GetProperty("id").GetString() ?? "Unknown"
            : "No Game!";

        return new StreamMetadata(streamId, title, gameId, startedAt);
    }
}