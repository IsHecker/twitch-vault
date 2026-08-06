using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using TwitchVault.Api.Auth;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Events;
using TwitchVault.Api.Persistence;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Common;
using TwitchVault.Api.Endpoints.Testing;
using TwitchLib.EventSub.Webhooks.Extensions;
using TwitchLib.EventSub.Webhooks.Core.Models;
using TwitchVault.Api.Backblaze;
using Amazon.S3;
using Amazon.Runtime;
using TwitchVault.Api.ChannelMonitor;

namespace TwitchVault.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddTwitchVaultServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));

        services.AddAuthenticationInternal(configuration);
        services.AddBackblazeStorage(configuration);

        services.AddHttpClient<ITwitchGqlClient, TwitchGqlClient>();
        services.AddHttpClient("TwitchHelixClient");
        services.AddSingleton<TwitchHelixClient>();

        services.AddSingleton<IDateTimeProvider, EgyptTimeProvider>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<EventBus>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<IStreamService, StreamService>();

        services.AddSingleton<JsonDatabase>();

        services.AddSingleton<IChannelRepository, ChannelRepository>();
        services.AddSingleton<IStreamRepository, StreamRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IUserChannelRepository, UserChannelRepository>();

        services.AddTransient<SegmentStateTracker>();
        services.AddTransient<IStreamFinalizer, StreamFinalizer>();
        services.AddTransient<ISegmentDownloader, SegmentDownloader>();
        services.AddTransient<IManifestPoller, ManifestPoller>();
        services.AddTransient<IThumbnailManager, ThumbnailManager>();

        services.AddSingleton<RecordingOrchestrator>();
        services.AddSingleton<IStreamRecorderRegistry, StreamRecorderRegistry>();
        services.AddSingleton<IStreamRecorderFactory, StreamRecorderFactory>();

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
        services.ConfigureOptions<BackblazeUploadJobConfiguration>();
        services.ConfigureOptions<LocalCleanupJobConfiguration>();

        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        services.AddEndpoints(Assembly.GetExecutingAssembly());

        services.AddSingleton<HlsPlaylistTestHarness>();
        return services;
    }

    private static IServiceCollection AddAuthenticationInternal(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddSingleton<TokenGeneratorService>();

        services.AddAuthentication(options =>
        {
            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = false,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidAudience = jwtOptions.Audience,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(jwtOptions.Secret))
            };
        });

        services.AddAuthorizationBuilder()
            .AddPolicy("Admin", policy => policy.RequireRole("Admin"));

        return services;
    }

    private static IServiceCollection AddBackblazeStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var backblazeOptions = configuration.GetSection(BackblazeStorageOptions.SectionName).Get<BackblazeStorageOptions>()!;
        services.Configure<BackblazeStorageOptions>(configuration.GetSection(BackblazeStorageOptions.SectionName));

        services.AddSingleton<IAmazonS3>(sp =>
        {
            return new AmazonS3Client(
                new BasicAWSCredentials(backblazeOptions.KeyId, backblazeOptions.ApplicationKey),
                new AmazonS3Config
                {
                    ServiceURL = backblazeOptions.Host,
                    AuthenticationRegion = backblazeOptions.AuthenticationRegion,
                    ForcePathStyle = true
                });
        });

        services.AddSingleton<BackblazeStorageService>();
        services.AddSingleton<BackblazeUploadProgressService>();
        services.AddSingleton<BackblazePlaylistRewriter>();

        return services;
    }
}