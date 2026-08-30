using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Polly.RateLimiting;

namespace TwitchVault.Api.Common;

public class ClientResilienceOptions
{
    public int MaxRetryAttempts { get; set; }
    public TimeSpan BaseDelay { get; set; }
    public int TotalRequests { get; set; }
    public TimeSpan ResetWindow { get; set; }
    public int QueueLimit { get; set; }
    public TimeSpan RequestTimeout { get; set; }
}

public static class ResilienceExtensions
{
    public static IHttpClientBuilder AddThrottle(
        this IHttpClientBuilder builder,
        Action<ClientResilienceOptions> configure)
    {
        var options = new ClientResilienceOptions();
        configure.Invoke(options);

        var localLimiter = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
        {
            TokenLimit = options.TotalRequests,
            TokensPerPeriod = 1,
            ReplenishmentPeriod = options.ResetWindow / options.TotalRequests,
            QueueLimit = options.QueueLimit,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        builder.AddResilienceHandler($"{builder.Name}-ThrottledPipeline", pipelineBuilder =>
        {
            // Rate limiter MUST be outermost so every attempt (including retries) acquires a token.
            // If placed inside the retry, retry attempts bypass the local limiter entirely.
            pipelineBuilder.AddRateLimiter(localLimiter);
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Linear,
                UseJitter = true,
                Delay = options.BaseDelay,
                DelayGenerator = args =>
                {
                    var headers = args.Outcome.Result?.Headers;
                    if (headers is not null)
                    {
                        // 1. Twitch: 'Ratelimit-Reset' (Unix timestamp in seconds)
                        if (headers.TryGetValues("Ratelimit-Reset", out var resetVals) &&
                            long.TryParse(resetVals.FirstOrDefault(), out var resetUnix))
                        {
                            var delay = DateTimeOffset.FromUnixTimeSeconds(resetUnix) - DateTimeOffset.UtcNow;
                            if (delay > TimeSpan.Zero)
                                return ValueTask.FromResult<TimeSpan?>(delay);
                        }

                        // 2. Discord: 'x-ratelimit-reset-after' (Seconds as float)
                        if (headers.TryGetValues("x-ratelimit-reset-after", out var afterVals) &&
                            float.TryParse(afterVals.FirstOrDefault(), out var resetAfterSeconds))
                        {
                            var delay = TimeSpan.FromSeconds(resetAfterSeconds + 0.25f);
                            if (delay > TimeSpan.Zero)
                                return ValueTask.FromResult<TimeSpan?>(delay);
                        }
                    }

                    return ValueTask.FromResult<TimeSpan?>(null);
                },
                ShouldHandle = args =>
                {
                    var isLocalRateLimitReject = args.Outcome.Exception is RateLimiterRejectedException;
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
                    return ValueTask.FromResult(isLocalRateLimitReject || isNetworkError || isTransientHttpError);
                }
            });
            pipelineBuilder.AddTimeout(options.RequestTimeout);
        });

        return builder;
    }
}