namespace TwitchVault.Api.Common;

public interface IStorageService
{
    bool Exists(string path);
    Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);
    Stream OpenWrite(string path, FileMode mode);
}

public sealed class PhysicalStorageService : IStorageService
{
    public bool Exists(string path) => File.Exists(path);

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllLinesAsync(path, cancellationToken);

    public Stream OpenWrite(string path, FileMode mode)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new FileStream(
            path,
            mode,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
    }
}