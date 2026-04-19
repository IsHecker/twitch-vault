using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Configuration;
using TwitchVault.Api.Models;

namespace TwitchVault.Api.Services;

public class SettingsService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private AppSettings _settings;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Settings => _settings;

    public SettingsService(IOptions<PathsOptions> options)
    {
        _filePath = options.Value.Settings;
        _settings = Load();
    }

    private AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public async Task UpdateAsync(AppSettings updated)
    {
        await _lock.WaitAsync();
        try
        {
            _settings = updated;
            Flush();
        }
        finally { _lock.Release(); }
    }

    private void Flush() =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_settings, JsonOptions));

    private void Write(Action<AppSettings> mutation)
    {
        _lock.Wait();
        try
        {
            mutation(_settings);
            Flush();
        }
        finally { _lock.Release(); }
    }
}