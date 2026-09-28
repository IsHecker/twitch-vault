using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Polly;
using Polly.Timeout;
using PolyStore;
using PolyStore.Catbox;
using PolyStore.Discord;
using PolyStore.Telegram;
using Quartz;
using TwitchLib.EventSub.Webhooks.Core.Models;
using TwitchLib.EventSub.Webhooks.Extensions;

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
            .AddAuthFeature(configuration)
            .AddTwitchFeature()
            .AddChannelsFeature()
            .AddRecordingFeature()
            .AddStreamsFeature()
            .AddStorageFeature(configuration)
            .AddBackgroundJobs()
            .AddRateLimiting();

        services.AddSingleton<LiveTestSession>();
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

    private static IServiceCollection AddChannelsFeature(this IServiceCollection services)
    {
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IChannelBanService, ChannelBanService>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services.ConfigureOptions<ChannelMonitorJobConfiguration>();
        return services;
    }

    private static IServiceCollection AddRecordingFeature(this IServiceCollection services)
    {
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
        services.AddHostedService<RecordingLifecycleService>();

        return services;
    }

    private static IServiceCollection AddStreamsFeature(this IServiceCollection services)
    {
        services.AddSingleton<IStreamService, StreamService>();

        return services;
    }

    private static IServiceCollection AddStorageFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.ConfigureOptions<StorageCleanupJobConfiguration>();
        services.ConfigureOptions<StorageUploadJobConfiguration>();
        services.ConfigureOptions<PublicVodCleanupJobConfiguration>();

        services.AddPolyStore(configuration)
            .AddDiscord()
            .AddCatbox()
            .AddTelegram();

        return services;
    }

    private static IServiceCollection AddTwitchFeature(this IServiceCollection services)
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
                    var isTimeout = args.Outcome.Exception is TimeoutRejectedException || args.Outcome.Exception is TimeoutException;
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
                    return ValueTask.FromResult(isNetworkError || isTimeout || isTransientHttpError);
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
        services.AddSingleton<ITwitchSubscriptionService, TwitchSubscriptionService>();
        services.AddHostedService<TwitchWebhookStartupService>();

        return services;
    }

    private static IServiceCollection AddAuthFeature(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var jwtOptions = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<GoogleAuthOptions>(configuration.GetSection(GoogleAuthOptions.SectionName));

        services.AddSingleton<TokenGeneratorService>();
        services.AddScoped<GoogleAuthService>();

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

    private static IServiceCollection AddRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.AddSlidingWindowLimiter("AuthRateLimit", options =>
            {
                options.PermitLimit = 10;
                options.Window = TimeSpan.FromMinutes(1);
                options.SegmentsPerWindow = 6;
                options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                options.QueueLimit = 0;
            });

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetTokenBucketLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 50,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        TokensPerPeriod = 10,
                        AutoReplenishment = true,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    }));

            limiter.OnRejected = async (context, ct) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/problem+json";
                await context.HttpContext.Response.WriteAsync(
                    """{"title":"Too Many Requests","status":429,"detail":"Rate limit exceeded. Try again later."}""",
                    ct);
            };
        });

        return services;
    }

    private static IServiceCollection AddBackgroundJobs(this IServiceCollection services)
    {
        services.ConfigureOptions<TwitchWebhookHealthCheckJobConfiguration>();

        services.AddQuartz();
        services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        return services;
    }
}

public sealed class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal Principal =>
        httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No authenticated user in scope.");

    public Guid Id => Principal.GetUserId();
    public bool IsAdmin => Principal.IsInRole("Admin");
}