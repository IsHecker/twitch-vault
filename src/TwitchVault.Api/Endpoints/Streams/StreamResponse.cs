using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Endpoints.Streams;

public record ChapterResponse(
    string Title,
    string CategoryId,
    DateTime StartedAt,
    DateTime? FinishedAt
)
{
    public static ChapterResponse FromDomain(Chapter chapter) =>
        new(
            chapter.Title,
            chapter.CategoryId,
            chapter.StartedAt,
            chapter.FinishedAt
        );
}

public record StreamFolderResponse(
    string RelativePath,
    string ThumbnailPath
)
{
    public static StreamFolderResponse FromDomain(StreamFolder folder) =>
        new(
            folder.RelativePath,
            folder.ThumbnailPath
        );
}

public record StreamResponse(
    string TwitchStreamId,
    string ChannelId,
    StreamFolderResponse Folder,
    string ThumbnailUrl,
    StreamStatus Status,
    bool MarkForDeletion,
    DateTime StartedAt,
    DateTime? FinishedAt,
    List<ChapterResponse> Chapters
)
{
    public static StreamResponse FromDomain(Domain.Stream stream) =>
        new(
            stream.TwitchStreamId,
            stream.ChannelId,
            StreamFolderResponse.FromDomain(stream.Folder),
            stream.ThumbnailUrl,
            stream.Status,
            stream.MarkForDeletion,
            stream.StartedAt,
            stream.FinishedAt,
            stream.Chapters.Select(ChapterResponse.FromDomain).ToList()
        );
}