using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using Telegram.Bot;
using TwitchLib.EventSub.Webhooks.Core.Models;
using TwitchLib.EventSub.Webhooks.Extensions;
using TwitchVault.Api.Auth;
using TwitchVault.Api.ChannelMonitor;
using TwitchVault.Api.CloudStorage;
using TwitchVault.Api.CloudStorage.Catbox;
using TwitchVault.Api.CloudStorage.Discord;
using TwitchVault.Api.CloudStorage.Jobs;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;

namespace TwitchVault.Api.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddTwitchVaultServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddConfigurationOptions(configuration)
            .AddCommonInfrastructure()
            .AddDatabase(configuration)
            .AddAuthenticationInternal(configuration)
            .AddTwitchAndEventSub()
            .AddRecording()
            .AddCloudStorage(configuration)
            .AddBackgroundJobs();

        services.AddSingleton<Endpoints.Testing.LiveTestSession>();
        services.AddEndpoints(Assembly.GetExecutingAssembly());

        return services;
    }

    private static IServiceCollection AddConfigurationOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PathsOptions>(configuration.GetSection(PathsOptions.SectionName));
        services.Configure<VaultOptions>(configuration.GetSection(VaultOptions.SectionName));
        services.Configure<TwitchOptions>(configuration.GetSection(TwitchOptions.SectionName));
        services.Configure<BackgroundJobsOptions>(configuration.GetSection(BackgroundJobsOptions.SectionName));
        services.Configure<RuntimeSettings>(configuration);

        return services;
    }

    private static IServiceCollection AddCommonInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDateTimeProvider, EgyptTimeProvider>();
        services.AddSingleton<IFileSystem, PhysicalFileSystem>();
        services.AddSingleton<EventBus>();

        return services;
    }

    private static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddPooledDbContextFactory<AppDbContext>(options =>
        {
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));
        });

        services.AddScoped<IAppDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContext());

        services.AddSingleton<IChannelRepository, ChannelRepository>();
        services.AddSingleton<IStreamRepository, StreamRepository>();
        services.AddSingleton<IUserRepository, UserRepository>();
        services.AddSingleton<IUserChannelRepository, UserChannelRepository>();

        return services;
    }

    private static IServiceCollection AddTwitchAndEventSub(this IServiceCollection services)
    {
        services.AddHttpClient<ITwitchGqlClient, TwitchGqlClient>();
        services.AddSingleton<TwitchHelixClient>();

        services.AddHttpClient(nameof(TwitchHelixClient))
            .AddThrottledResilience(opts =>
            {
                opts.PermitLimit = 700;
                opts.Window = TimeSpan.FromMinutes(1);
                opts.QueueLimit = 5;
                opts.MaxRetryAttempts = 3;
                opts.BaseDelay = TimeSpan.FromSeconds(3);
                opts.RequestTimeout = TimeSpan.FromSeconds(30);
            });

        services.AddOptions<TwitchLibEventSubOptions>()
            .Configure<IOptions<TwitchOptions>>((options, twitchOptions) =>
            {
                options.Secret = twitchOptions.Value.Secret;
                options.CallbackPath = twitchOptions.Value.WebhookPath;
            });

        services.AddTwitchLibEventSubWebhooks(options => { });
        services.AddSingleton<TwitchSubscriptionService>();
        services.AddHostedService<TwitchWebhookStartupService>();

        return services;
    }

    private static IServiceCollection AddRecording(this IServiceCollection services)
    {
        services.AddSingleton<IStreamService, StreamService>();
        services.AddSingleton<IRecordingOrchestrator, RecordingOrchestrator>();
        services.AddSingleton<IStreamRecorderRegistry, StreamRecorderRegistry>();
        services.AddSingleton<IStreamRecorderFactory, StreamRecorderFactory>();

        services.AddTransient<SegmentStateTracker>();
        services.AddTransient<ChapterTracker>();
        services.AddTransient<IStreamFinalizer, StreamFinalizer>();
        services.AddTransient<ISegmentStore, SegmentStore>();
        services.AddTransient<IManifestPoller, ManifestPoller>();
        services.AddTransient<IThumbnailManager, ThumbnailManager>();
        services.AddTransient<IStreamStorageService, StreamStorageService>();
        services.AddTransient<ISegmentUploader, LiveSegmentUploader>();

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

    private static IServiceCollection AddCloudStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.ConfigureOptions<StorageCleanupJobConfiguration>();

        services.AddSingleton<StreamJobCoordinator>();
        services.AddSingleton<ProgressTracker>();
        services.AddSingleton<StorageRouter>();
        services.AddSingleton<StorageProviderRegistry>();
        services.AddSingleton<ICloudStorageService, CloudStorageService>();
        services.AddSingleton<CatboxApiClient>();

        var storageOptions = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()!;
        foreach (var instance in storageOptions.Instances)
        {
            if (instance.RateLimit is null)
                continue;

            services.AddHttpClient(instance.Name)
                .AddThrottledResilience(opts =>
                {
                    opts.PermitLimit = instance.RateLimit.PermitLimit;
                    opts.Window = TimeSpan.FromSeconds(instance.RateLimit.WindowSeconds);
                    opts.QueueLimit = instance.RateLimit.QueueLimit;
                    opts.MaxRetryAttempts = instance.RateLimit.MaxRetryAttempts;
                    opts.BaseDelay = TimeSpan.FromSeconds(instance.RateLimit.BaseDelaySeconds);
                    opts.RequestTimeout = TimeSpan.FromSeconds(instance.RateLimit.RequestTimeoutSeconds);
                });

            if (instance.Provider == CloudProviderType.Telegram)
                services.AddSingleton<ITelegramBotClient>((sp) =>
                {
                    var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(instance.Name);
                    var clientOptions = new TelegramBotClientOptions(instance.Credentials.AccessToken!)
                    {
                        RetryThreshold = 60,
                        RetryCount = instance.RateLimit?.MaxRetryAttempts ?? 3
                    };
                    return new TelegramBotClient(clientOptions, http);
                });
        }

        return services;
    }

    private static IServiceCollection AddBackgroundJobs(this IServiceCollection services)
    {
        services.ConfigureOptions<ChannelMonitorJobConfiguration>();
        services.ConfigureOptions<TwitchWebhookHealthCheckJobConfiguration>();

        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }
}