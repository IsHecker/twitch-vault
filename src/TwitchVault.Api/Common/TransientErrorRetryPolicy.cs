namespace TwitchVault.Api.Common;

public static class TransientErrorRetryPolicy
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan _networkErrorDelay = TimeSpan.FromSeconds(2);

    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var attempt = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (IsTransient(ex) && ++attempt < MaxAttempts)
            {
                await Task.Delay(_networkErrorDelay, cancellationToken);
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