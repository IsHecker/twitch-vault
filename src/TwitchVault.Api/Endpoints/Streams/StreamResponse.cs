using TwitchVault.Api.Domain;

namespace TwitchVault.Api.Endpoints.Streams;

public record ChapterResponse(
    string Title,
    string CategoryId,
    DateTime StartedAt,
    DateTime? FinishedAt)
{
    public static ChapterResponse FromDomain(Chapter chapter) =>
        new(
            chapter.Title,
            chapter.CategoryId,
            chapter.StartedAt,
            chapter.FinishedAt
        );
}

public record StreamResponse(
    string TwitchStreamId,
    string ChannelId,
    string ThumbnailUrl,
    StreamStatus Status,
    bool MarkForDeletion,
    DateTime StartedAt,
    DateTime? FinishedAt,
    List<ChapterResponse> Chapters)
{
    public static StreamResponse FromDomain(Domain.Stream stream) =>
        new(
            stream.TwitchStreamId,
            stream.ChannelId,
            stream.ThumbnailUrl,
            stream.Status,
            stream.MarkForDeletion,
            stream.StartedAt,
            stream.FinishedAt,
            stream.Chapters.Select(ChapterResponse.FromDomain).ToList()
        );
}