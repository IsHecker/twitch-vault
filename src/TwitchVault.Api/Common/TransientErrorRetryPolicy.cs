namespace TwitchVault.Api.Common;

public sealed class TransientErrorRetryPolicy(int maxAttempts, TimeSpan delay, ILogger<TransientErrorRetryPolicy> logger)
{
    public async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsTransient(ex) && ++attempt < maxAttempts)
            {
                logger?.LogWarning(ex, "Transient error on attempt {Attempt}/{Max}. Retrying in {Delay}.",
                    attempt, maxAttempts, delay);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private static bool IsTransient(Exception ex) => ex switch
    {
        OperationCanceledException => false,
        HttpRequestException => true,
        TimeoutException => true,
        _ => false
    };
}