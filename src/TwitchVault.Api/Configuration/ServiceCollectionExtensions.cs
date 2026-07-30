using System.Reflection;
using Quartz;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Events;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.ChannelMonitor;
using TwitchVault.Api.Common;
using TwitchVault.Api.Endpoints.Testing;
using TwitchLib.EventSub.Webhooks.Extensions;
using TwitchLib.EventSub.Webhooks.Core.Models;

namespace TwitchVault.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTwitchVaultServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));

        services.AddHttpClient<ITwitchGqlClient, TwitchGqlClient>();
        services.AddHttpClient("TwitchHelixClient");
        services.AddSingleton<TwitchHelixClient>();

        services.AddSingleton<IDateTimeProvider, EgyptTimeProvider>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<SettingsService>();

        services.AddSingleton<JsonDatabase>();

        services.AddSingleton<IChannelRepository, ChannelRepository>();
        services.AddSingleton<IStreamRepository, StreamRepository>();
        services.AddSingleton<IStreamService, StreamService>();

        services.AddTransient<SegmentStateTracker>();
        services.AddTransient<IStreamFinalizer, StreamFinalizer>();
        services.AddTransient<ISegmentDownloader, SegmentDownloader>();
        services.AddTransient<IManifestPoller, ManifestPoller>();
        services.AddTransient<IThumbnailManager, ThumbnailManager>();

        services.AddSingleton<RecordingOrchestrator>();
        services.AddSingleton<IStreamRecorderRegistry, StreamRecorderRegistry>();
        services.AddSingleton<IStreamRecorderFactory, StreamRecorderFactory>();

        // EventSub webhook via TwitchLib
        services.AddOptions<TwitchLibEventSubOptions>()
            .Configure<SettingsService>((options, settingsService) =>
            {
                options.Secret = settingsService.Settings.Twitch.Secret;
                options.CallbackPath = settingsService.Settings.Twitch.WebhookPath;
            });

        services.AddTwitchLibEventSubWebhooks(options => { });
        services.AddSingleton<TwitchSubscriptionService>();
        services.AddHostedService<TwitchWebhookStartupService>();

        services.ConfigureOptions<ChannelMonitorJobConfiguration>();
        services.ConfigureOptions<TwitchWebhookHealthCheckJobConfiguration>();

        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddEndpoints(Assembly.GetExecutingAssembly());

        services.AddSingleton<HlsPlaylistTestHarness>();
        return services;
    }
}