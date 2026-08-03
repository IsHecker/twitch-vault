using System.Collections.Concurrent;
using TwitchVault.Api.Common;
using TwitchVault.Api.Recording.HLS;

namespace TwitchVault.Api.Endpoints.Testing;

public sealed class HlsPlaylistTestHarness(IDateTimeProvider dateTimeProvider, IStorageService fileSystem) : IAsyncDisposable
{
    private const string DefaultStreamId = "test-stream";

    private readonly ConcurrentDictionary<string, HlsPlaylist> _playlists = new();
    private readonly ConcurrentDictionary<string, int> _segmentCounters = new();

    public static string Normalize(string? streamId) => streamId ?? DefaultStreamId;

    public string FolderFor(string streamId)
    {
        var folder = Path.Combine("hls-test", streamId);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public async Task<HlsPlaylist> GetOrCreateAsync(string streamId, CancellationToken ct)
    {
        if (_playlists.TryGetValue(streamId, out var existing))
            return existing;

        var playlist = await HlsPlaylist.LoadOrCreateAsync(FolderFor(streamId), dateTimeProvider, fileSystem, ct);
        return _playlists.GetOrAdd(streamId, playlist);
    }

    public string NextSegmentFileName(string streamId)
    {
        var next = _segmentCounters.AddOrUpdate(streamId, 1, (_, count) => count + 1);
        return $"seg_{next}.ts";
    }

    public async Task ResetAsync(string streamId)
    {
        if (_playlists.TryRemove(streamId, out var playlist))
            await playlist.DisposeAsync();

        _segmentCounters.TryRemove(streamId, out _);

        var folder = FolderFor(streamId);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var playlist in _playlists.Values)
            await playlist.DisposeAsync();

        _playlists.Clear();
    }
}

public class InitTestPlaylist : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/test/playlist/init", async (
            string? streamId,
            HlsPlaylistTestHarness harness,
            CancellationToken ct) =>
        {
            var id = HlsPlaylistTestHarness.Normalize(streamId);
            var playlist = await harness.GetOrCreateAsync(id, ct);

            if (!playlist.HasInitSegment)
                await playlist.SetInitSegmentAsync("init.mp4", ct);

            return Results.Ok(new
            {
                streamId = id,
                folder = harness.FolderFor(id),
                playlist.HasInitSegment
            });
        })
        .WithName(nameof(InitTestPlaylist))
        .WithTags("Testing")
        .WithSummary("Create or resume a test HLS playlist and set its init segment")
        .Produces(StatusCodes.Status200OK);
}

public class AddTestSegment : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/test/playlist/segment", async (
            string? streamId,
            float? durationSeconds,
            HlsPlaylistTestHarness harness,
            CancellationToken ct) =>
        {
            var id = HlsPlaylistTestHarness.Normalize(streamId);
            var playlist = await harness.GetOrCreateAsync(id, ct);

            var fileName = harness.NextSegmentFileName(id);
            var duration = durationSeconds ?? 10f;

            playlist.UpdateTwitchMediaSequence(playlist.LastTwitchMediaSequence + 1);
            await playlist.AddSegmentAsync(fileName, duration, ct);

            return Results.Ok(new
            {
                streamId = id,
                fileName,
                duration,
                playlist.LastTwitchMediaSequence
            });
        })
        .WithName(nameof(AddTestSegment))
        .WithTags("Testing")
        .WithSummary("Append one sequentially-numbered segment to the test playlist")
        .Produces(StatusCodes.Status200OK);
}
public class AddTestDiscontinuity : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/test/playlist/discontinuity", async (
            string? streamId,
            HlsPlaylistTestHarness harness,
            CancellationToken ct) =>
        {
            var id = HlsPlaylistTestHarness.Normalize(streamId);
            var playlist = await harness.GetOrCreateAsync(id, ct);

            await playlist.AddDiscontinuityAsync(ct);

            return Results.Ok(new { streamId = id, discontinuityAdded = true });
        })
        .WithName(nameof(AddTestDiscontinuity))
        .WithTags("Testing")
        .WithSummary("Add a discontinuity marker to the test playlist")
        .Produces(StatusCodes.Status200OK);
}
public class GetTestPlaylistRaw : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/test/playlist/raw", async (
            string? streamId,
            HlsPlaylistTestHarness harness,
            CancellationToken ct) =>
        {
            var id = HlsPlaylistTestHarness.Normalize(streamId);
            var path = Path.Combine(harness.FolderFor(id), "playlist.m3u8");

            if (!File.Exists(path))
                return Results.NotFound("No playlist yet. Call /api/test/playlist/init first.");

            var content = await File.ReadAllTextAsync(path, ct);
            return Results.Text(content, "text/plain");
        })
        .WithName(nameof(GetTestPlaylistRaw))
        .WithTags("Testing")
        .WithSummary("Dump the current test playlist.m3u8 content as plain text")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
}
public class ResetTestPlaylist : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapPost("/api/test/playlist/reset", async (
            string? streamId,
            HlsPlaylistTestHarness harness,
            CancellationToken ct) =>
        {
            var id = HlsPlaylistTestHarness.Normalize(streamId);
            await harness.ResetAsync(id);

            return Results.Ok(new { streamId = id, reset = true });
        })
        .WithName(nameof(ResetTestPlaylist))
        .WithTags("Testing")
        .WithSummary("Delete the test playlist's folder so you can start clean")
        .Produces(StatusCodes.Status200OK);
}