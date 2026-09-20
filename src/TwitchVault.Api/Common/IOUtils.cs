namespace TwitchVault.Api.Common;

public static class IOUtils
{
    private const int MaxRetries = 5;
    private const int DelayMs = 500;

    public static async Task DeleteDirectoryAsync(string path)
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
            catch (Exception) when (i < MaxRetries - 1)
            {
                await Task.Delay(DelayMs);
            }
        }

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}