namespace TwitchVault.Api.CloudStorage;

public readonly record struct StorageFile
{
    public string FileName { get; }
    public string ContentType { get; }
    public Stream Content { get; }

    public StorageFile(string fileName, string contentType, Stream content)
    {
        FileName = SanitizeFileName(fileName);
        ContentType = contentType;
        Content = content;
    }

    public static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("FileName cannot be null or whitespace.", nameof(fileName));

        var baseName = Path.GetFileName(fileName);
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(baseName.Where(c => !invalidChars.Contains(c)).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
            throw new ArgumentException($"FileName '{fileName}' is invalid after sanitization.", nameof(fileName));

        return cleaned;
    }
}