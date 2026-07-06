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

namespace TwitchVault.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTwitchVaultServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));

        services.AddHttpClient<ITwitchGqlClient, TwitchGqlClient>();
        services.AddHttpClient<TwitchHelixClient>();

        services.AddSingleton<IDateTimeProvider, DateTimeProvider>();
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
        services.AddSingleton<TwitchWebSocketClient>();
        services.AddSingleton<TwitchSubscriptionService>();
        services.AddSingleton<IStreamRecorderRegistry, StreamRecorderRegistry>();
        services.AddSingleton<IStreamRecorderFactory, StreamRecorderFactory>();

        services.ConfigureOptions<ChannelMonitorJobConfiguration>();
        services.ConfigureOptions<TwitchEventSubJobConfiguration>();

        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddEndpoints(Assembly.GetExecutingAssembly());


        services.AddSingleton<HlsPlaylistTestHarness>();
        return services;
    }
}