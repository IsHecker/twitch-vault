namespace TwitchVault.Api.Common;

public interface IFileSystem
{
    bool Exists(string path);
    Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default);
    System.IO.Stream OpenWrite(string path, FileMode mode);
}

public sealed class PhysicalFileSystem : IFileSystem
{
    public bool Exists(string path) => File.Exists(path);

    public Task<string[]> ReadAllLinesAsync(string path, CancellationToken cancellationToken = default)
        => File.ReadAllLinesAsync(path, cancellationToken);

    public System.IO.Stream OpenWrite(string path, FileMode mode)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        return new FileStream(
            path,
            mode,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 65536,
            useAsync: true);
    }
}