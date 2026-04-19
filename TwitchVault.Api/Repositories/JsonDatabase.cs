using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;

namespace TwitchVault.Api.Repositories;

public class JsonDatabase
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly AppDatabase _cache;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonDatabase(IOptions<PathsOptions> options)
    {
        _filePath = options.Value.Database;
        _cache = Load();
    }

    public async Task<AppDatabase> ReadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return _cache;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task WriteAsync(Action<AppDatabase> mutation)
    {
        await _lock.WaitAsync();
        try
        {
            mutation(_cache);
            Flush();
        }
        finally
        {
            _lock.Release();
        }
    }

    private AppDatabase Load()
    {
        if (!File.Exists(_filePath))
            return new AppDatabase();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<AppDatabase>(json, JsonOptions) ?? new AppDatabase();
    }

    private void Flush()
    {
        var json = JsonSerializer.Serialize(_cache, JsonOptions);
        File.WriteAllText(_filePath, json);
    }
}