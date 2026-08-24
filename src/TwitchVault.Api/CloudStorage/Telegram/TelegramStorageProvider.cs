using Telegram.Bot;
using Telegram.Bot.Types;
using TwitchVault.Api.Common.Results;

namespace TwitchVault.Api.CloudStorage.Telegram;

public sealed class TelegramOptions
{
    public string ChannelId { get; init; } = null!;
    public string BotToken { get; init; } = null!;
    public string CDNUrl { get; init; } = null!;
}

[StorageProvider(CloudProviderType.Telegram, typeof(TelegramOptions))]
public sealed class TelegramCloudStorageProvider(
    ITelegramBotClient client,
    LiveOptions<StorageInstanceOptions> instanceOptions,
    LiveOptions<TelegramOptions> telegramOptions,
    ILogger<TelegramCloudStorageProvider> logger) : ICloudStorageProvider
{
    private readonly ChatId _storageChatId = new(long.Parse(telegramOptions.Value.ChannelId));

    public StorageInstanceOptions Options => instanceOptions.Value;
    private TelegramOptions TelegramOptions => telegramOptions.Value;

    public async Task<Result<IEnumerable<RemoteUrl>>> UploadAsync(
        IEnumerable<StorageFile> files, CancellationToken cancellationToken)
    {
        var items = files as IReadOnlyList<StorageFile> ?? files.ToArray();
        if (items.Count == 0)
            return Array.Empty<RemoteUrl>();

        var behavior = Options.Behavior;

        var oversized = items.Where(f => f.Content.CanSeek && f.Content.Length > behavior.MaxFileSizeBytes);
        if (oversized.Any())
            return Error.Failure($"'{oversized.First().FileName}' exceeds MaxFileSizeBytes ({behavior.MaxFileSizeBytes}).");

        var chunkSize = Math.Clamp(behavior.MaxUploadBatchSize, 1, behavior.MaxUploadBatchSize);
        var chunks = BatchUtils.Chunk(items, chunkSize).ToList();

        var uploaded = new List<RemoteUrl>();

        foreach (var chunk in chunks)
        {
            try
            {
                uploaded.AddRange(await UploadGroupAsync(chunk, cancellationToken));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Upload chunk of {Count} failed on instance {Instance}.", chunk.Count, Options.Name);
                return Error.Failure($"[{string.Join(", ", chunk.Select(c => c.FileName))}]: {ex.Message}");
            }
        }

        return uploaded;
    }

    private async Task<IReadOnlyList<RemoteUrl>> UploadGroupAsync(IReadOnlyList<StorageFile> chunk, CancellationToken ct)
    {
        var media = chunk
            .Select(f => (IAlbumInputMedia)new InputMediaDocument(InputFile.FromStream(f.Content, f.FileName)))
            .ToArray();

        var messages = await client.SendMediaGroup(_storageChatId, media, cancellationToken: ct);

        // Telegram returns messages in the same order the media array was submitted.
        return chunk.Zip(messages, (file, msg) =>
            new RemoteUrl(file.FileName, ToRemoteUrl(msg.MessageId, msg.Document!.FileId))).ToList();
    }

    public async Task<Result> DeleteAsync(IEnumerable<string> remoteUrls, CancellationToken cancellationToken)
    {
        var messageIds = remoteUrls.Select(ExtractMessageId);

        if (!messageIds.Any())
            return Result.Success;

        var behavior = Options.Behavior;

        foreach (var chunk in messageIds.Chunk(behavior.MaxDeleteBatchSize))
        {
            try
            {
                await client.DeleteMessages(_storageChatId, chunk, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Delete chunk of {Count} failed on instance {Instance}.", chunk.Length, Options.Name);
                return Error.Failure($"[{string.Join(", ", chunk)}]: {ex.Message}");
            }
        }

        return Result.Success;
    }

    private string ToRemoteUrl(int messageId, string fileId) => $"{TelegramOptions.CDNUrl}/{messageId}/{fileId}";

    private static int ExtractMessageId(string url) => int.Parse(url.Split('/')[^2]);
}

public static class BatchUtils
{
    public static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
    {
        if (size <= 0)
            size = 1;

        for (var i = 0; i < source.Count; i += size)
            yield return source.Skip(i).Take(size).ToArray();
    }
}