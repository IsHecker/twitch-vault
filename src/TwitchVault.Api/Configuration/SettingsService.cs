using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Configuration;

public class SettingsService
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly AppSettings _settings;

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

    public async Task UpdateAsync(AppSettings request)
    {
        await _lock.WaitAsync();
        try
        {
            _settings.Vault.MaxSegmentDurationInSec = request.Vault.MaxSegmentDurationInSec;
            _settings.Vault.MaxConsecutiveEmptyPolls = request.Vault.MaxConsecutiveEmptyPolls;

            _settings.Twitch.ClientId = request.Twitch.ClientId;
            _settings.Twitch.Authorization = request.Twitch.Authorization;

            _settings.BackgroundJobs[JobOptions.ChannelMonitor].Enabled =
                request.BackgroundJobs[JobOptions.ChannelMonitor].Enabled;
            _settings.BackgroundJobs[JobOptions.ChannelMonitor].RunIntervalInMinutes =
                request.BackgroundJobs[JobOptions.ChannelMonitor].RunIntervalInMinutes;

            _settings.BackgroundJobs[JobOptions.TwitchWebhookHealthCheck].Enabled =
                request.BackgroundJobs[JobOptions.TwitchWebhookHealthCheck].Enabled;
            _settings.BackgroundJobs[JobOptions.TwitchWebhookHealthCheck].RunIntervalInMinutes =
                request.BackgroundJobs[JobOptions.TwitchWebhookHealthCheck].RunIntervalInMinutes;

            _settings.BackgroundJobs[JobOptions.StorageUpload].Enabled =
                request.BackgroundJobs[JobOptions.StorageUpload].Enabled;
            _settings.BackgroundJobs[JobOptions.StorageUpload].RunIntervalInMinutes =
                request.BackgroundJobs[JobOptions.StorageUpload].RunIntervalInMinutes;

            Flush();
        }
        finally
        {
            _lock.Release();
        }
    }

    private void Flush() =>
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_settings, JsonOptions));
}