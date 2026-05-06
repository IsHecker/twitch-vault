using System.Text.Json;
using TwitchVault.Api.Services;

namespace TwitchVault.Api.Twitch;

public sealed class TwitchClient(
    HttpClient httpClient,
    SettingsService settingsService)
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

    public async Task<string> GetMasterPlaylistAsync(
        string channel,
        CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.PlaybackToken(channel);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);

        var token = document!.RootElement.GetProperty("data")
            .GetProperty("streamPlaybackAccessToken")
            .Deserialize<PlaybackToken>();

        var masterPlaylistUrl = BuildMasterPlaylistUrl(channel, token);
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

    public async Task<(string? StreamId, string? VodId)> GetStreamVODIdAsync(string channel, CancellationToken cancellationToken)
    {
        var payload = TwitchGqlPayloads.GetStreamVOD(channel);
        using var response = await SendGqlRequestAsync(payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return (null, null);

        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);

        var stream = document!.RootElement.GetProperty("data").GetProperty("user").GetProperty("stream");
        if (stream.ValueKind == JsonValueKind.Null)
            return (null, null);

        var streamId = stream.GetProperty("id").GetString();
        var archiveVideo = stream.GetProperty("archiveVideo");
        var vodId = archiveVideo.ValueKind != JsonValueKind.Null ? archiveVideo.GetProperty("id").GetString() : null;

        return (streamId, vodId);
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

        request.Headers.Add("Client-Id", Options.ClientId);
        request.Headers.Add("Authorization", Options.Authorization);

        request.Headers.Add("Accept", "*/*");
        request.Headers.Add("Accept-Language", "en-US");
        request.Headers.Add("Client-Session-Id", "7c9e031af8864dcb");
        request.Headers.Add("Client-Version", "bb20717f-bafe-4854-92db-64e522efc13d");
        request.Headers.Add("X-Device-Id", "hr3zoVzUji7t6bVuXT4784lLs1cUJR4x");
        request.Headers.Add("Referer", "https://www.twitch.tv/");

        return httpClient.SendAsync(request, cancellationToken);
    }

    private static string BuildMasterPlaylistUrl(string channel, PlaybackToken token) =>
        $"https://usher.ttvnw.net/api/v2/channel/hls/{channel}.m3u8" +
        $"?acmb=eyJBcHBWZXJzaW9uIjoiYmIyMDcxN2YtYmFmZS00ODU0LTkyZGItNjRlNTIyZWZjMTNkIiwiQ2xpZW50QXBwIjoid2ViIn0%3D" +
        $"&allow_source=true&browser_family=chrome&browser_version=146.0&cdm=wv&enable_score=true" +
        $"&fast_bread=true&include_unavailable=true&lang=en&multigroup_video=false&os_name=Windows" +
        $"&os_version=NT%2010.0&p=5517154&platform=web&play_session_id=0e7f21f7fdff40c09bd7c7865ed8beff" +
        $"&player_backend=mediaplayer&player_version=1.50.0-rc.4&playlist_include_framerate=true" +
        $"&reassignments_supported=true&sig={token.Signature}&supported_codecs=av1,h265,h264" +
        $"&token={token.Token}&transcode_mode=cbr_v1";

    private static StreamMetadata? ParseStreamMetadata(JsonElement root)
    {
        var data = root.GetProperty("data");
        var user = data.GetProperty("user");
        
        var hasStream = user.TryGetProperty("stream", out var stream) && stream.ValueKind != JsonValueKind.Null;
        var hasLastBroadcast = user.TryGetProperty("lastBroadcast", out var lastBroadcast) && lastBroadcast.ValueKind != JsonValueKind.Null;

        if (!hasStream && !hasLastBroadcast)
            return null;

        var streamId = hasStream ? stream.GetProperty("id").GetString() : string.Empty;
        var previewImageUrl = hasStream && stream.TryGetProperty("previewImageURL", out var previewUrl) 
            ? previewUrl.GetString() 
            : string.Empty;

        var metadataSource = hasLastBroadcast ? lastBroadcast : stream;
        var streamTitle = metadataSource.GetProperty("title").GetString() ?? string.Empty;

        var game = metadataSource.GetProperty("game");
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