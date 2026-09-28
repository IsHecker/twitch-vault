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
            pipelineBuilder.AddRateLimiter(localLimiter);
            pipelineBuilder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = options.MaxRetryAttempts,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = options.BaseDelay,
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