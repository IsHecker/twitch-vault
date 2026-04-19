using System.Reflection;
using Quartz;
using TwitchVault.Api.ChannelMonitor;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Events;
using TwitchVault.Api.Repositories;
using TwitchVault.Api.Services;
using TwitchVault.Api.Twitch;

namespace TwitchVault.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTwitchVaultServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));

        services.AddHttpClient<TwitchClient>();

        services.AddSingleton<EventBus>();
        services.AddSingleton<SettingsService>();

        services.AddSingleton<JsonDatabase>();

        services.AddSingleton<ChannelRepository>();
        services.AddSingleton<StreamRepository>();

        services.AddTransient<SegmentDownloader>();

        services.AddSingleton<StreamController>();

        services.ConfigureOptions<ChannelMonitorJobConfiguration>();
        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddEndpoints(Assembly.GetExecutingAssembly());
        return services;
    }
}