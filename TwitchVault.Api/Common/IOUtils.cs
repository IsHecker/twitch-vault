namespace TwitchVault.Api.Common;

public static class IOUtils
{
    private const int MaxRetries = 5;
    private const int DelayMs = 500;

    public static async Task DeleteDirectoryWithRetriesAsync(string path)
    {
        if (!Directory.Exists(path))
            return;
        for (int i = 0; i < MaxRetries; i++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (i < MaxRetries - 1)
            {
                await Task.Delay(DelayMs);
            }
            catch (UnauthorizedAccessException) when (i < MaxRetries - 1)
            {
                await Task.Delay(DelayMs);
            }
        }

        // Final attempt without catching to let the exception bubble up if it still fails
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}