using System.Text.Json;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Twitch;

public sealed class TwitchGqlClient(
    HttpClient httpClient,
    SettingsService settingsService) : ITwitchGqlClient
{
    private const string TwitchGqlUrl = "https://gql.twitch.tv/gql";
    private TwitchOptions Options => settingsService.Settings.Twitch;

    public async Task<StreamMetadata?> GetStreamMetadataAsync(string channel, CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.StreamMetadata(channel);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);

        return ParseStreamMetadata(document!.RootElement);
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
        var results = new Dictionary<Domain.Channel, StreamMetadata?>();

        for (var i = 0; i < channels.Count; i++)
        {
            results[channels[i]] = ParseStreamMetadata(document!.RootElement[i]);
        }

        return results;
    }

    public async Task<string> GetMasterPlaylistAsync(string channel, CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.PlaybackToken(channel);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);

        var token = document!.RootElement.GetProperty("data")
            .GetProperty("streamPlaybackAccessToken")
            .Deserialize<PlaybackToken>();

        var masterPlaylistUrl = BuildMasterPlaylistUrl(channel, token!);
        var playlistResponse = await httpClient.GetAsync(masterPlaylistUrl, cancellationToken);
        if (!playlistResponse.IsSuccessStatusCode)
            return string.Empty;

        return await playlistResponse.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetPlaylistContentAsync(string playlistUrl, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(playlistUrl, cancellationToken);
        return response.IsSuccessStatusCode ?
            await response.Content.ReadAsStringAsync(cancellationToken)
            : string.Empty;
    }

    public async Task<Stream> DownloadAsStreamAsync(string url, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
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

    public async Task<string?> GetVODThumbnailUrlAsync(string vodId, CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.VideoMetadata(vodId);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);

        var video = document!.RootElement.GetProperty("data").GetProperty("video");
        if (video.ValueKind == JsonValueKind.Null)
            return null;

        var url = video.GetProperty("previewThumbnailURL").GetString();
        return url is null || url.Contains("404_preview") ? null : url;
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

    private Task<HttpResponseMessage> SendGqlRequestAsync(object payload, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, TwitchGqlUrl)
        {
            Content = JsonContent.Create(payload)
        };

        request.Headers.TryAddWithoutValidation("Client-Id", Options.ClientId);
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36");

        request.Headers.TryAddWithoutValidation("Origin", "https://www.twitch.tv");
        request.Headers.TryAddWithoutValidation("Accept", "*/*");
        request.Headers.TryAddWithoutValidation("Accept-Language", "en-US");
        request.Headers.TryAddWithoutValidation("Client-Session-Id", "7c9e031af8864dcb");
        request.Headers.TryAddWithoutValidation("Client-Version", "aa5594d1-b8dc-4533-8262-11a5a0e9955f");
        request.Headers.TryAddWithoutValidation("X-Device-Id", "hr3zoVzUji7t6bVuXT4784lLs1cUJR4x");
        request.Headers.TryAddWithoutValidation("Referer", "https://www.twitch.tv/");
        request.Headers.TryAddWithoutValidation("Authority", "gql.twitch.tv");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua", "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
        request.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
        request.Headers.TryAddWithoutValidation("sec-fetch-dest", "empty");
        request.Headers.TryAddWithoutValidation("sec-gpc", "1");

        return httpClient.SendAsync(request, cancellationToken);
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

        var hasStream = user.TryGetProperty("stream", out var stream) && stream.ValueKind != JsonValueKind.Null;

        if (!hasStream)
            return null;

        var streamId = hasStream ? stream.GetProperty("id").GetString() : string.Empty;
        var previewImageUrl = hasStream && stream.TryGetProperty("previewImageURL", out var previewUrl)
            ? previewUrl.GetString()
            : string.Empty;

        var streamTitle = stream.GetProperty("title").GetString() ?? string.Empty;
        var game = stream.GetProperty("game");
        var categoryName = game.ValueKind != JsonValueKind.Null
            ? game.GetProperty("name").GetString() ?? "Unknown"
            : "Unknown";

        return new StreamMetadata(
            streamId ?? string.Empty,
            previewImageUrl ?? string.Empty,
            streamTitle,
            categoryName);
    }
}