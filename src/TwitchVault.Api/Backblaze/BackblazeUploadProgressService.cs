using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;

namespace TwitchVault.Api.Backblaze;

public sealed class BackblazeUploadProgressService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, int> _progress;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public BackblazeUploadProgressService(
        IWebHostEnvironment env,
        IOptions<PathsOptions> pathsOptions)
    {
        var streamsRoot = Path.Combine(env.ContentRootPath, pathsOptions.Value.Streams);
        Directory.CreateDirectory(streamsRoot);
        _filePath = Path.Combine(streamsRoot, "upload_progress.json");
        _progress = Load();
    }

    public int GetLastUploadedSegmentIndex(string twitchStreamId)
    {
        return _progress.TryGetValue(twitchStreamId, out var index) ? index : -1;
    }

    public async Task SaveProgressAsync(string twitchStreamId, int segmentIndex)
    {
        await _lock.WaitAsync();
        try
        {
            _progress[twitchStreamId] = segmentIndex;
            await FlushAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RemoveProgressAsync(string twitchStreamId)
    {
        if (!_progress.TryRemove(twitchStreamId, out _))
            return;

        await FlushAsync();
    }

    private ConcurrentDictionary<string, int> Load()
    {
        if (!File.Exists(_filePath))
            return [];

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<ConcurrentDictionary<string, int>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task FlushAsync()
    {
        var tempFile = $"{_filePath}.tmp";
        var json = JsonSerializer.Serialize(_progress, JsonOptions);
        await File.WriteAllTextAsync(tempFile, json);
        File.Move(tempFile, _filePath, overwrite: true);
    }
}