using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Quartz;
using TwitchLib.EventSub.Webhooks.Core.Models;
using TwitchLib.EventSub.Webhooks.Extensions;
using TwitchVault.Api.Auth;
using TwitchVault.Api.ChannelMonitor;
using CloudStorage.Core;
using CloudStorage.Core.Discord;
using CloudStorage.Core.Telegram;
using TwitchVault.Api.Storage.Jobs;
using TwitchVault.Api.Common;
using TwitchVault.Api.Domain;
using TwitchVault.Api.Endpoints;
using TwitchVault.Api.Events;
using TwitchVault.Api.Recording;
using TwitchVault.Api.Recording.HLS;
using TwitchVault.Api.Twitch;
using TwitchVault.Api.Twitch.EventSub;
using CloudStorage.Core.Catbox;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using System.Net;

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
            .AddBackgroundJobs();

        services.AddCloudStorageSystem(configuration);

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
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"), sql => sql.EnableRetryOnFailure())
                .LogTo(_ => { }, LogLevel.None);
        });

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                .UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll)
                .LogTo(_ => { }, LogLevel.None);
        });

        services.AddSingleton<IDataStore, EfDataStore>();
        return services;
    }

    private static IServiceCollection AddTwitchAndEventSub(this IServiceCollection services)
    {
        services.AddHttpClient(TwitchHttpClients.Api)
        .ConfigureHttpClient((sp, client) =>
        {
            var twitchOptions = sp.GetRequiredService<IOptionsMonitor<TwitchOptions>>().CurrentValue;
            client.DefaultRequestHeaders.TryAddWithoutValidation("Client-Id", twitchOptions.PublicClientId);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            MaxConnectionsPerServer = 100
        });

        services.AddHttpClient(TwitchHttpClients.Cdn)
        .ConfigureHttpClient((sp, client) =>
        {
            var twitchOptions = sp.GetRequiredService<IOptionsMonitor<TwitchOptions>>().CurrentValue;

            client.DefaultRequestHeaders.TryAddWithoutValidation("Client-Id", twitchOptions.PublicClientId);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://www.twitch.tv");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "en-US");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Client-Session-Id", "7c9e031af8864dcb");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Client-Version", "aa5594d1-b8dc-4533-8262-11a5a0e9955f");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-Device-Id", "hr3zoVzUji7t6bVuXT4784lLs1cUJR4x");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://www.twitch.tv/");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authority", "gql.twitch.tv");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua",
                "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-fetch-dest", "empty");
            client.DefaultRequestHeaders.TryAddWithoutValidation("sec-gpc", "1");
        })
        .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            KeepAlivePingDelay = TimeSpan.FromSeconds(60),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingPolicy = HttpKeepAlivePingPolicy.WithActiveRequests,
            MaxConnectionsPerServer = 500
        })
        .AddResilienceHandler($"{TwitchHttpClients.Cdn}-RetryPipeline", pipelineBuilder =>
        {
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromMilliseconds(500),
                ShouldHandle = args =>
                {
                    var isNetworkError = args.Outcome.Exception is HttpRequestException;
                    var isTransientHttpError = (args.Outcome.Result?.StatusCode) switch
                    {
                        HttpStatusCode.TooManyRequests => true,
                        HttpStatusCode.RequestTimeout => true,
                        HttpStatusCode.InternalServerError => true,
                        HttpStatusCode.BadGateway => true,
                        HttpStatusCode.ServiceUnavailable => true,
                        HttpStatusCode.GatewayTimeout => true,
                        _ => false
                    };
                    return ValueTask.FromResult(isNetworkError || isTransientHttpError);
                }
            });
            pipelineBuilder.AddTimeout(TimeSpan.FromSeconds(20));
        });

        services.AddSingleton<ITwitchGqlClient, TwitchGqlClient>();

        services.AddSingleton<TwitchHelixClient>();
        services.AddHttpClient(nameof(TwitchHelixClient)).AddThrottle(
            opts =>
            {
                opts.TotalRequests = 100;
                opts.ResetWindow = TimeSpan.FromMinutes(1);
                opts.QueueLimit = 50;
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
        services.AddScoped<IChannelService, ChannelService>();

        services.AddSingleton<IStreamService, StreamService>();
        services.AddSingleton<IRecordingOrchestrator, RecordingOrchestrator>();
        services.AddSingleton<IStreamRecorderRegistry, StreamRecorderRegistry>();
        services.AddSingleton<IStreamRecorderFactory, StreamRecorderFactory>();
        services.AddSingleton<IStreamStorageService, StreamStorageService>();

        services.AddTransient<SegmentStateTracker>();
        services.AddTransient<IChapterTracker, ChapterTracker>();
        services.AddTransient<IStreamFinalizer, StreamFinalizer>();
        services.AddTransient<ISegmentStore, SegmentStore>();
        services.AddTransient<IManifestPoller, ManifestPoller>();
        services.AddTransient<IThumbnailManager, ThumbnailManager>();
        services.AddTransient<ISegmentUploader, LiveSegmentUploader>();

        services.AddSingleton<IUploadQueue, UploadQueue>();
        services.AddHostedService<UploadQueueBackgroundService>();

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

    private static IServiceCollection AddCloudStorageSystem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.ConfigureOptions<StorageCleanupJobConfiguration>();
        services.ConfigureOptions<StorageUploadJobConfiguration>();

        services.AddCloudStorage(configuration)
            .AddDiscordStorage()
            .AddCatboxStorage()
            .AddTelegramStorage();

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